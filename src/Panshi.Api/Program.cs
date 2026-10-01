using Microsoft.AspNetCore.StaticFiles;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Panshi.Common.Results;
using Microsoft.AspNetCore.Authorization;
using Panshi.Api.Authorization;
using Hangfire;
using Hangfire.PostgreSql;
using Panshi.Api.Filters;
using Panshi.Api.Jobs;
using Panshi.Api.Hubs;
using Panshi.Api.Middleware;
using Panshi.Api.Security;
using Panshi.Api.Services;
using Panshi.Common.Cache;
using Panshi.Common.Json;
using Panshi.Common.Realtime;
using Panshi.Common.Runtime;
using Panshi.Repository;
using Panshi.Service.Auth;
using Panshi.Service.Biz;
using Panshi.Service.Flow;
using Panshi.Service.Md;
using Panshi.Service.Scm;
using Panshi.Service.Base;
using Panshi.Service.Sys;
using Serilog;
using SqlSugar;
using Microsoft.AspNetCore.HttpOverrides;

// PG/Npgsql：DateTime.Now（Local）写入 timestamp without time zone
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddControllers(o =>
    {
        o.Filters.Add<ApiResultFilter>();
        o.Filters.Add<OperationLogFilter>();
        o.Filters.Add<NoRepeatSubmitFilter>();
    })
    .AddJsonOptions(o => JsonConfig.Create(o.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx => new ObjectResult(
        ApiResult.Fail(400, ctx.ModelState.SelectMany(kv => kv.Value?.Errors ?? []).Select(e => e.ErrorMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "请求参数校验失败"))
        { StatusCode = StatusCodes.Status400BadRequest });

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();

// 登录/验证码接口限流（防爆破辅助，账号锁定为主）。分区键 = 解析后的真实来源 IP。
// ⚠️ 直连/容器 NAT 拓扑下所有客户端会共用同一个对端地址，此时 PermitLimit 实际是「整站每分钟」的量——
// 反代后面务必同时开 Features:TrustForwardedHeaders 并登记 Security:TrustedProxies，否则第 11 次登录就挡住所有人。
builder.Services.AddRateLimiter(rl =>
{
    rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var loginPermit = builder.Configuration.GetValue("RateLimit:LoginPermit", 10);
    var captchaPermit = builder.Configuration.GetValue("RateLimit:CaptchaPermit", 60);
    // 白名单命中时给 NoLimiter 分区：IpGuard 中间件跑在本策略之前，所以标记此时已就位
    var keyed = (Microsoft.AspNetCore.Http.HttpContext http, int permit) =>
        http.Items.ContainsKey(IpGuardMiddleware.WhitelistedKey)
            ? RateLimitPartition.GetNoLimiter("whitelist")
            : RateLimitPartition.GetFixedWindowLimiter(ClientIp.PartitionKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromMinutes(1) });
    rl.AddPolicy("login", http => keyed(http, loginPermit));
    rl.AddPolicy("captcha", http => keyed(http, captchaPermit));

    // 被拒必须留痕：没有这条日志，阈值是松是紧只能靠猜；留痕也才谈得上将来按 IP 做黑名单
    rl.OnRejected = async (ctx, ct) =>
    {
        var http = ctx.HttpContext;
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Security.RateLimit")
            .LogWarning("429 限流拒绝 policy={Policy} ip={Ip} path={Path} ua={Ua}",
                http.GetEndpoint()?.Metadata
                    .GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName ?? "-",
                ClientIp.Of(http), http.Request.Path.Value, http.Request.Headers.UserAgent.ToString());

        // 与 OnChallenge/OnForbidden 同一套路：直写信封，别让 429 变成前端读不懂的空体
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.WriteAsync(
            JsonSerializer.Serialize(ApiResult.Fail(429, "操作过于频繁，请稍后再试"), JsonConfig.Options), ct);
    };
});

builder.Services.AddCors(cors => cors.AddPolicy("web", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    .WithExposedHeaders("X-Captcha-Id", "X-Captcha-Enabled", "Content-Disposition")));

builder.Services.AddPanshiAuth(builder.Configuration);
// 图形验证码只要「字符清楚可读」：关掉默认配置里的干扰线与气泡噪点（内部系统不需要对抗式难度）。
builder.Services.AddCaptcha(o =>
{
    o.ImageOption.InterferenceLineCount = 0;
    o.ImageOption.BubbleCount = 0;
});
builder.Services.AddSingleton<Panshi.Common.Cache.ICacheService, Panshi.Common.Cache.MemoryCacheService>();
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddScoped<ConfigService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<DataScopeService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<DeptService>();
builder.Services.AddScoped<PositionService>();
builder.Services.AddScoped<DictService>();
builder.Services.AddScoped<ConfigAdminService>();
builder.Services.AddScoped<NoticeService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<LogService>();
builder.Services.AddScoped<OnlineService>();
builder.Services.AddScoped<IpGuardService>();
builder.Services.AddSingleton<LogCleanupJob>();
builder.Services.AddSingleton<NoticePublishJob>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<JobManagementService>();
builder.Services.AddScoped<FlowEngineService>();
builder.Services.AddScoped<FlowAdminService>();
builder.Services.AddScoped<FlowQueryService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<PurchaseService>();
builder.Services.AddScoped<MaterialService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<PriceAgreementService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<SalesOrderService>();
builder.Services.AddScoped<StockDocService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<FileAccessService>();
// 红线 #7：IFlowBusinessHandler 多实现必须 AddScoped（TryAdd 只收第一个）
builder.Services.AddScoped<IFlowBusinessHandler, ExpenseFlowHandler>();
builder.Services.AddScoped<IFlowBusinessHandler, PurchaseFlowHandler>();
builder.Services.AddScoped<IFlowBusinessHandler, PurchaseOrderFlowHandler>();
builder.Services.AddScoped<IFlowBusinessHandler, SalesOrderFlowHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermAuthorizationHandler>();
builder.Services.AddScoped<INotifyService, NotifyService>();

// 反向代理（nginx/Traefik 等）后：读取 X-Forwarded-For / X-Forwarded-Proto，
// 让日志、限流、SignalR 拿到真实客户端 IP 与 https 协议。默认关闭（直连本地测试不受影响），
// 部署到反代后面时设 Features:TrustForwardedHeaders=true，并把直连本服务的那一跳填进 Security:TrustedProxies。
// ⚠️ 旧写法是 KnownIPNetworks.Clear() + KnownProxies.Clear()，那是「相信任何来源自带的 XFF」——
// 来源 IP 就成了客户端说了算的东西：按 IP 限流可绕、将来按 IP 拉黑可被投毒。现在换成显式受信名单，
// 且开关与名单缺一即启动失败（配置期炸一次，胜过运行期默默信了不该信的人）。
var trustForwarded = builder.Configuration.GetValue("Features:TrustForwardedHeaders", false);
var trustedProxies = ClientIp.ReadTrustedProxies(builder.Configuration);
if (trustForwarded)
{
    if (trustedProxies.Count == 0)
        throw new InvalidOperationException(
            "Features:TrustForwardedHeaders=true 但 Security:TrustedProxies 为空。" +
            "这样任何客户端自带的 X-Forwarded-For 都会被采信，来源 IP 可被随意伪造。" +
            "请把直连本服务的反代地址填进 Security:TrustedProxies（IP 或 CIDR，逗号分隔），或把该开关设回 false。");

    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // 开了这个开关，名单就是唯一的真相：连默认的「信任回环」也清掉。
        // 否则本机 curl/本机任意进程都能替你声明来源 IP，「受信名单」沦为建议性的、且不可验证。
        // 本机反代请显式写进 Security:TrustedProxies（如 127.0.0.1）。
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var net in trustedProxies) o.KnownIPNetworks.Add(net);
    });
}

builder.Services.AddSingleton<ISqlSugarClient>(_ => SqlSugarSetup.CreateDb(new DbOptions
{
    ConnectionString = builder.Configuration["Db:ConnectionString"]
                       ?? throw new InvalidOperationException("缺少配置 Db:ConnectionString"),
    SnowflakeWorkerId = ushort.TryParse(builder.Configuration["Db:SnowflakeWorkerId"], out var w) ? w : (ushort)1
}));
builder.Services.AddScoped(typeof(IRepository<>), typeof(SqlSugarRepository<>));

var hangfireConn = builder.Configuration["Db:ConnectionString"]!;
builder.Services.AddHangfire(h => h.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(hangfireConn)));
builder.Services.AddHangfireServer(o => o.WorkerCount = 2);

// 开发期在 Build 时就遍历整张依赖图：控制器/服务构造函数缺注册会当场抛出，
// 而不是等首个请求打进来才 500（FileAccessService 漏注册就是这么溜过测试的）。
builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = builder.Environment.IsDevelopment());

var app = builder.Build();

// 反代后最先应用，确保后续限流/日志/鉴权读到真实来源
if (trustForwarded)
    app.UseForwardedHeaders();
app.Logger.LogInformation("来源 IP 解析：{Mode}", trustForwarded
    ? $"采信 XFF，受信代理 {trustedProxies.Count} 条（名单外来源自带的 XFF 一律忽略）"
    : "不采信 XFF（直连形态；若实际在反代/容器 NAT 后面，所有客户端会共用一个来源 IP）");

// Hangfire 作业激活器：每次执行创建 DI 作用域
JobActivator.Current = new Panshi.Api.Jobs.ScopedJobActivator(
    app.Services.GetRequiredService<IServiceScopeFactory>());

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors("web");
// 名单闸门排在限流之前：注定要被拒的请求，不必再花一次限流与鉴权的成本
app.UseMiddleware<IpGuardMiddleware>();
app.UseRateLimiter();

app.MapOpenApi();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/openapi/v1.json", "磐石 API");
    o.RoutePrefix = "swagger";
});

// 入口 HTML 强制协商缓存：避免浏览器缓存旧 index.html（引用已失效的 hash 资源）导致白屏。
// /assets 带内容 hash 可长缓存；HTML 一律 no-cache（见下方 StaticFileOptions.OnPrepareResponse）。
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path == "/" ||
            string.Equals(System.IO.Path.GetExtension(ctx.Context.Request.Path.Value ?? ""), ".html",
                StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});
app.UseAuthentication();
app.UseAuthorization();

// 认证通过后写入 AsyncLocal 操作者上下文（AOP/审计/日志取用）
app.Use(async (ctx, next) =>
{
    var uid = ctx.CurrentUserId();
    if (uid == 0)
    {
        await next();
        return;
    }

    var dept = long.TryParse(ctx.User.FindFirstValue(AuthClaimTypes.Dept), out var d) ? (long?)d : null;
    using var _ = OperationUser.Use(new OperationContext(uid, ctx.User.FindFirstValue(ClaimTypes.Name), dept));
    await next();
});

app.MapControllers();
app.MapHub<NotifyHub>("/hubs/notify");

if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire", new DashboardOptions { DashboardTitle = "磐石定时任务" });

// SPA 静态托管（单容器）：/api 未匹配必须 404（不能被首页吞成 JSON 之外的响应），其余非静态路径回前端首页（no-cache）。
// ⚠️ 不要为 /assets 注册显式端点——会抢在 UseStaticFiles 之前拦截，导致真实静态资源 404。
app.Map("/api/{*path}", () => Results.NotFound(new { code = 404, msg = "接口不存在", data = (object?)null }));
var spaIndex = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
app.MapFallback(async ctx =>
{
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.ContentType = "text/html";
    if (File.Exists(spaIndex))
        await ctx.Response.SendFileAsync(spaIndex);
    else
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
});
app.MapHealthChecks("/api/v1/health");

// —— 启动数据库引导（等待 PG 就绪 → 逐表 CodeFirst → 迁移 → 种子）——
using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
    await DbInitializer.WaitForDatabaseAsync(db, ct: app.Lifetime.ApplicationStopping);
    DbInitializer.InitializeTables(db, msg => Log.Information(msg));
    DbMigrationRunner.Run(db, msg => Log.Information(msg));
    DbSeeder.Seed(db);
}

// —— 内置作业注册（蓝图§5.8）：DB 就绪后用 DI 的 IRecurringJobManager（JobStorage 此时已初始化，
//    容器冷启动下静态 RecurringJob 会在 DB 未就绪时抛 JobStorage.Current 异常）——
using (var scope = app.Services.CreateScope())
{
    var rm = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    var jobOptions = new RecurringJobOptions { TimeZone = TimeZoneInfo.Local };
    rm.AddOrUpdate<LogCleanupJob>("sys.log.cleanup", j => j.RunAsync(), "0 2 * * *", jobOptions);
    rm.AddOrUpdate<BackupService>("sys.backup.daily", j => j.RunAsync(), "0 3 * * *", jobOptions);
    rm.AddOrUpdate<NoticePublishJob>("sys.notice.publish", j => j.RunAsync(), "* * * * *", jobOptions);
    await scope.ServiceProvider.GetRequiredService<LogCleanupJob>().RunAsync(); // 启动即清一次过期会话（轻量）
}

Log.Information("磐石 API 启动完成，环境：{Env}", app.Environment.EnvironmentName);
app.Run();

/// <summary>供集成测试引用（WebApplicationFactory&lt;Program&gt;）。</summary>
public partial class Program;
