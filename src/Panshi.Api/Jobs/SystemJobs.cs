using System.Text.RegularExpressions;
using Hangfire;
using Panshi.Common.Realtime;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Serilog;
using SqlSugar;

namespace Panshi.Api.Jobs;

/// <summary>
/// 内置作业目录（蓝图§5.8：注册按 (JobId, Name, Cron, Expression) 定义数组循环）。
/// 失败站内信通知内置管理员 + 上抛自动重试（Hangfire 默认 10 次指数退避）。
/// </summary>
public static class JobCatalog
{
    public record Def(string JobId, string Name, string Cron);

    public static readonly Def[] All =
    [
        new("sys.log.cleanup", "日志与会话清理", "0 2 * * *"),
        new("sys.backup.daily", "数据库与附件每日备份", "0 3 * * *"),
        new("sys.notice.publish", "定时公告发布", "* * * * *")
    ];
}

/// <summary>sys.log.cleanup：操作/登录/变更日志 + 过期会话物理删除（保留期参数化）。</summary>
[DisableConcurrentExecution(60 * 30)]
public class LogCleanupJob(IServiceScopeFactory scopeFactory)
{
    public async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var keepDays = config.GetValue("Jobs:LogRetainDays", 90);
        var cutoff = DateTime.Now.AddDays(-keepDays);

        var oper = await db.Ado.ExecuteCommandAsync(
            "delete from sys_operation_log where create_time < @c", new SugarParameter("@c", cutoff));
        var login = await db.Ado.ExecuteCommandAsync(
            "delete from sys_login_log where create_time < @c", new SugarParameter("@c", cutoff));
        var change = await db.Ado.ExecuteCommandAsync(
            "delete from sys_change_log where create_time < @c", new SugarParameter("@c", cutoff));
        var sess = await db.Ado.ExecuteCommandAsync(
            "delete from sys_user_session where expire_time < @n", new SugarParameter("@n", DateTime.Now));

        Log.Information("日志清理完成：操作 {Oper} 登录 {Login} 变更 {Change} 会话 {Sess}", oper, login, change, sess);
    }
}

/// <summary>sys.notice.publish：到期定时公告 Status 2→1。</summary>
[DisableConcurrentExecution(120)]
public class NoticePublishJob(IServiceScopeFactory scopeFactory)
{
    public async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var n = await db.Ado.ExecuteCommandAsync(
            "update sys_notice set status = 1, update_time = now() where status = 2 and publish_time <= now() and is_deleted = false");
        if (n > 0) Log.Information("定时公告到期发布 {N} 条", n);
    }
}

/// <summary>
/// sys.backup.daily：pg_dump 全量备份（custom 格式）+ uploads 增量镜像 + 保留期清理；
/// 备份目录=api 与 db 容器共享挂载（Db:BackupDir）。失败站内信通知内置管理员。
/// </summary>
[DisableConcurrentExecution(60 * 60)]
public class BackupService(IServiceScopeFactory scopeFactory)
{
    public async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        try
        {
            await BackupCoreAsync(sp, config);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "每日备份失败");
            var notify = sp.GetRequiredService<INotifyService>();
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var admin = await db.Queryable<SysUser>().FirstAsync(u => u.UserName == "admin");
            if (admin is not null)
                await notify.NotifyUserAsync(admin.Id, "系统备份失败", ex.Message, "system");
            throw; // 上抛 → Hangfire 自动重试
        }
    }

    private static async Task BackupCoreAsync(IServiceProvider sp,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        var conn = config["Db:ConnectionString"] ?? "";
        var backupRoot = Path.Combine(AppContext.BaseDirectory, config["Db:BackupDir"] ?? "backups");
        var keepDays = config.GetValue("Jobs:BackupRetainDays", 14);
        Directory.CreateDirectory(backupRoot);

        var (host, port, database, user, password) = ParseConn(conn);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // ① pg_dump 全量（custom 格式，逻辑等价 BACKUP DATABASE）
        var dumpFile = Path.Combine(backupRoot, $"{database}_{stamp}.dump");
        var pgDump = FindPgDump();
        if (pgDump is not null)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(pgDump,
                $"-h {host} -p {port} -U {user} -d {database} -Fc -f \"{dumpFile}\"")
            {
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.Environment["PGPASSWORD"] = password;
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var err = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) throw new InvalidOperationException($"pg_dump 失败：{err}");
            Log.Information("数据库备份完成 {File}", Path.GetFileName(dumpFile));
        }
        else
        {
            Log.Warning("未找到 pg_dump 可执行文件，跳过数据库导出（容器部署请镜像内置 postgresql-client）");
        }

        // ② uploads 增量镜像
        var src = Path.Combine(AppContext.BaseDirectory, "uploads");
        if (Directory.Exists(src))
        {
            var dst = Path.Combine(backupRoot, "uploads");
            MirrorDirectory(src, dst);
        }

        // ③ 保留期清理
        foreach (var f in Directory.GetFiles(backupRoot, "*.dump"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-keepDays))
                File.Delete(f);
        var upMirror = Path.Combine(backupRoot, "uploads");
        if (Directory.Exists(upMirror))
            foreach (var f in Directory.GetFiles(upMirror, "*", SearchOption.AllDirectories))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-keepDays))
                    File.Delete(f);
    }

    private static void MirrorDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            var targetDir = Path.GetDirectoryName(target);
            if (targetDir is not null) Directory.CreateDirectory(targetDir);
            if (!File.Exists(target) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(target))
                File.Copy(file, target, true);
        }
    }

    private static string? FindPgDump()
    {
        foreach (var candidate in new[] { "pg_dump", "/usr/bin/pg_dump", "/usr/local/bin/pg_dump" })
            try
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(candidate,
                    "--version") { RedirectStandardOutput = true, CreateNoWindow = true });
                p?.Kill();
                return candidate;
            }
            catch
            {
                /* try next */
            }

        return null;
    }

    private static (string Host, int Port, string Db, string User, string Password) ParseConn(string conn)
    {
        string Get(string key, string def)
        {
            var m = Regex.Match(conn, $@"{key}\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : def;
        }

        return (Get("Host", Get("Server", "localhost")), int.Parse(Get("Port", "5432")), Get("Database", "panshi"),
            Get("Username", Get("User Id", "panshi")), Get("Password", ""));
    }
}

/// <summary>定时任务管理端点支撑（列表/立即触发/移除/恢复）。</summary>
public class JobManagementService
{
    private static readonly HashSet<string> Removed = [];

    public List<object> List()
    {
        return JobCatalog.All.Select(j => (object)new
        {
            jobId = j.JobId,
            name = j.Name,
            cron = j.Cron,
            enabled = !Removed.Contains(j.JobId),
            scheduler = j.JobId switch
            {
                "sys.log.cleanup" => "每日 02:00",
                "sys.backup.daily" => "每日 03:00",
                _ => "每分钟"
            }
        }).ToList();
    }

    public void Trigger(string jobId) => RecurringJob.TriggerJob(jobId);

    public void Toggle(string jobId, bool enable)
    {
        if (enable)
        {
            Removed.Remove(jobId);
            var def = JobCatalog.All.First(j => j.JobId == jobId);
            switch (jobId)
            {
                case "sys.log.cleanup":
                    RecurringJob.AddOrUpdate<LogCleanupJob>(jobId, j => j.RunAsync(), def.Cron,
                        new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
                    break;
                case "sys.backup.daily":
                    RecurringJob.AddOrUpdate<BackupService>(jobId, j => j.RunAsync(), def.Cron,
                        new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
                    break;
                default:
                    RecurringJob.AddOrUpdate<NoticePublishJob>(jobId, j => j.RunAsync(), def.Cron,
                        new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
                    break;
            }
        }
        else
        {
            Removed.Add(jobId);
            RecurringJob.RemoveIfExists(jobId);
        }
    }
}

/// <summary>Hangfire 作业激活器：每次执行创建 DI 作用域。</summary>
public class ScopedJobActivator(IServiceScopeFactory scopeFactory) : JobActivator
{
    public override JobActivatorScope BeginScope(JobActivatorContext context)
        => new Scope(scopeFactory.CreateScope());

    private sealed class Scope(IServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => scope.ServiceProvider.GetRequiredService(type);

        public override void DisposeScope() => scope.Dispose();
    }
}
