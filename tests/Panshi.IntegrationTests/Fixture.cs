using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Panshi.Common.Cache;
using Panshi.Common.Realtime;
using Panshi.Common.Runtime;
using Panshi.Model.Entities;
using Panshi.Repository;
using Panshi.Service.Base;
using Panshi.Service.Biz;
using Panshi.Service.Flow;
using Panshi.Service.Md;
using Panshi.Service.Scm;
using Panshi.Service.Sys;
using SqlSugar;

namespace Panshi.IntegrationTests;

/// <summary>空通知（收集推送记录供断言）。</summary>
public sealed class RecordingNotify : INotifyService
{
    private readonly List<(long Uid, string Title)> _notices = [];
    public IReadOnlyList<(long Uid, string Title)> Notices => _notices;
    public List<(long Uid, string Reason)> ForceLogouts { get; } = [];

    public Task NotifyUserAsync(long userId, string title, string? content, string msgType = "system",
        string? bizType = null, long? bizId = null)
    {
        _notices.Add((userId, title));
        return Task.CompletedTask;
    }

    public Task NotifyUsersAsync(IReadOnlyList<long> userIds, string title, string? content,
        string msgType = "business", string? bizType = null, long? bizId = null)
    {
        foreach (var u in userIds) _notices.Add((u, title));
        return Task.CompletedTask;
    }

    public Task ForceLogoutAsync(long userId, string reason)
    {
        ForceLogouts.Add((userId, reason));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 真实 PG 库 fixture：连 panshi_test（docker init 预建）→ 逐表 CodeFirst → 迁移 → 种子。
/// 连接串可用环境变量 PANSHI_TEST_CONN 覆盖。
/// </summary>
public class PgFixture : IAsyncLifetime
{
    public ISqlSugarClient Db { get; private set; } = null!;
    public RecordingNotify Notify { get; } = new();

    /// <summary>
    /// 测试库连接串。口令不放仓库里：优先 <c>PANSHI_TEST_CONN</c>，
    /// 否则按 docker compose 的习惯从仓库根 <c>.env</c> 取 <c>PANSHI_DB_PASSWORD</c> 拼出来
    /// （clone 完 <c>cp .env.example .env</c> 填口令就能直接 dotnet test，不必再导环境变量）。
    /// </summary>
    public static string Conn
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("PANSHI_TEST_CONN");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
            return $"Host=localhost;Port=5432;Database=panshi_test;Username=panshi;Password={EnvDbPassword()};Pooling=true";
        }
    }

    private static string EnvDbPassword()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".env"))) dir = dir.Parent;
        var line = dir is null ? "" : (File.ReadAllLines(Path.Combine(dir.FullName, ".env"))
            .FirstOrDefault(l => l.StartsWith("PANSHI_DB_PASSWORD=")) ?? "");
        var pwd = line[(line.IndexOf('=') + 1)..].Trim().Trim('"');
        return pwd.Length > 0 ? pwd : throw new InvalidOperationException(
            "集成测试要连真实 PG：既没有 PANSHI_TEST_CONN，也没在仓库根 .env 找到 PANSHI_DB_PASSWORD。"
            + "先 cp .env.example .env 填口令（或直接 export PANSHI_TEST_CONN）。");
    }

    public Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        Db = SqlSugarSetup.CreateDb(new DbOptions { ConnectionString = Conn, SnowflakeWorkerId = 3 });
        DbInitializer.WaitForDatabaseAsync(Db, 6, 2000).GetAwaiter().GetResult();
        DbInitializer.InitializeTables(Db);
        DbMigrationRunner.Run(Db);
        DbSeeder.Seed(Db);
        OperationUser.Use(new OperationContext(1, "admin", 2)); // 默认以种子管理员身份
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------- 服务装配（DI-lite） ----------------
    public IRepository<T> Repo<T>() where T : BaseEntity, new() => new SqlSugarRepository<T>(Db);

    /// <summary>
    /// 另开一套独立上下文。并发用例必须每个任务一套仓储——线上是「一个请求一个 DI 作用域一条连接」，
    /// 复用 fixture 的 Db 会让所有任务挤在同一条连接/同一个事务里，测出来的不是并发行为。
    /// </summary>
    public SqlSugarScope NewDb() => SqlSugarSetup.CreateDb(new DbOptions { ConnectionString = Conn, SnowflakeWorkerId = 3 });

    /// <summary>
    /// 并发用例专用：整条依赖链都挂在给定连接上。哪怕只漏一个共享仓储（物料/仓库/数据权限），
    /// 20 个任务挤同一根连接就会报「A command is already in progress」。
    /// </summary>
    public StockDocService StockDocsOn(ISqlSugarClient db)
        => new(new SqlSugarRepository<ScmStockDoc>(db), new SqlSugarRepository<ScmStockDocLine>(db),
            new SqlSugarRepository<ScmStock>(db), new SqlSugarRepository<ScmStockLedger>(db),
            new SqlSugarRepository<MdMaterial>(db), new SqlSugarRepository<MdWarehouse>(db),
            new DataScopeService(new SqlSugarRepository<SysUser>(db), new SqlSugarRepository<SysUserRole>(db),
                new SqlSugarRepository<SysRole>(db), new SqlSugarRepository<SysRoleDept>(db),
                new SqlSugarRepository<SysDept>(db)));

    public MemoryCacheService Cache() => new(new MemoryCache(new MemoryCacheOptions()));

    public ConfigService Config() => new(Repo<SysConfig>(), Cache());

    public PermissionService Permissions()
        => new(Repo<SysUser>(), Repo<SysUserRole>(), Repo<SysRole>(), Repo<SysRoleMenu>(), Repo<SysMenu>(), Cache());

    public DataScopeService DataScope()
        => new(Repo<SysUser>(), Repo<SysUserRole>(), Repo<SysRole>(), Repo<SysRoleDept>(), Repo<SysDept>());

    public List<IFlowBusinessHandler> BizHandlers()
        =>
        [
            new ExpenseFlowHandler(Repo<BizExpense>()),
            new PurchaseFlowHandler(Repo<BizPurchaseRequest>()),
            // 订单审批回调靠 BusinessTable 匹配，这里漏注册=集成测试走不到回调（与 Program.cs 是两处清单）
            new PurchaseOrderFlowHandler(Repo<ScmPurchaseOrder>(), Repo<ScmPurchaseOrderLine>(), Arrivals()),
            new SalesOrderFlowHandler(Repo<ScmSalesOrder>(), Repo<ScmSalesOrderLine>())
        ];

    public FlowEngineService Engine()
        => new(Repo<SysFlowInstance>(), Repo<SysFlowTask>(), Repo<SysFlowRecord>(), Repo<SysFlowCc>(),
            Repo<SysFlowDefinition>(), Repo<SysFlowBinding>(), Repo<SysUser>(), Repo<SysDept>(), Repo<SysRole>(),
            Repo<SysUserRole>(), Repo<SysPosition>(), Repo<SysUserPosition>(), Notify, BizHandlers());

    public ExpenseService Expenses()
        => new(Repo<BizExpense>(), Engine(), DataScope());

    public MaterialService Materials() => new(Repo<MdMaterial>());

    public SupplierService Suppliers() => new(Repo<MdSupplier>());

    public CustomerService Customers() => new(Repo<MdCustomer>());

    public WarehouseService Warehouses() => new(Repo<MdWarehouse>());

    public PriceAgreementService Prices()
        => new(Repo<MdPriceAgreement>(), Repo<MdSupplier>(), Repo<MdMaterial>());

    public StockDocService StockDocs()
        => new(Repo<ScmStockDoc>(), Repo<ScmStockDocLine>(), Repo<ScmStock>(), Repo<ScmStockLedger>(),
            Repo<MdMaterial>(), Repo<MdWarehouse>(), DataScope());

    public StockService Stocks() => new(Repo<ScmStock>());

    public LedgerService Ledgers() => new(Repo<ScmStockLedger>());

    public StockAlertService StockAlerts()
        => new(Repo<MdMaterial>(), Repo<MdWarehouse>(), Repo<ScmStock>());

    public StockSummaryService StockSummary()
        => new(Repo<ScmStockLedger>(), Repo<MdMaterial>(), Repo<MdWarehouse>());

    public SupplierPerformanceService SupplierPerf()
        => new(Repo<ScmPurchaseOrder>(), Repo<ScmStockDoc>());

    public ArrivalService Arrivals()
        => new(Repo<ScmPurchaseArrival>(), Repo<ScmPurchaseOrder>(), Repo<ScmPurchaseOrderLine>(),
            Repo<ScmStockDoc>(), Repo<ScmStockDocLine>(), DataScope());

    public PurchaseOrderService PurchaseOrders()
        => new(Repo<ScmPurchaseOrder>(), Repo<ScmPurchaseOrderLine>(), Repo<MdSupplier>(), Repo<MdMaterial>(),
            Repo<BizPurchaseRequest>(), Engine(), DataScope(), Arrivals());

    public SalesOrderService SalesOrders()
        => new(Repo<ScmSalesOrder>(), Repo<ScmSalesOrderLine>(), Repo<MdCustomer>(), Repo<MdMaterial>(),
            Repo<MdWarehouse>(), Engine(), DataScope());

    public DeptService Depts() => new(Repo<SysDept>(), Repo<SysUser>());

    public UserService UserService()
        => new(Repo<SysUser>(), Repo<SysUserRole>(), Repo<SysUserPosition>(), Repo<SysRole>(), Repo<SysDept>(),
            Repo<SysUserSession>(), Config(), DataScope(), Notify);

    /// <summary>按给定开关装配一个 IP 名单服务（DryRun / 受信代理都可在测试里模拟）。</summary>
    public IpGuardService IpGuard(bool dryRun = false, string[]? trustedProxies = null)
        => new(Repo<SysIpRule>(), Cache(), new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Security:IpGuard:DryRun"] = dryRun ? "true" : "false",
                ["Security:IpGuard:Enabled"] = "true",
                ["Security:TrustedProxies"] = string.Join(',', trustedProxies ?? [])
            }).Build());

    public OnlineService Online() => new(Repo<SysUserSession>(), Repo<SysUser>(), DataScope());

    public LogService Logs()
        => new(Repo<SysOperationLog>(), Repo<SysLoginLog>(), Repo<SysChangeLog>(), DataScope());

    public FlowQueryService FlowQuery()
        => new(Repo<SysFlowInstance>(), Repo<SysFlowTask>(), Repo<SysFlowRecord>(), Repo<SysFlowCc>(),
            Repo<SysFlowDefinition>(), Permissions());

    public FileAccessService FileAccess()
        => new(Repo<SysFile>(), Repo<BizExpense>(), Repo<BizPurchaseRequest>(), DataScope());

    public AuthService Auth()
        => new(Repo<SysUser>(), Repo<SysUserRole>(), Repo<SysRole>(), Repo<SysRoleMenu>(), Repo<SysMenu>(),
            Repo<SysDept>(), Repo<SysUserSession>(), Repo<SysLoginLog>(), Token(), Config(), Notify);

    public Panshi.Service.Auth.TokenService Token()
        => new(new Panshi.Service.Auth.AuthOptions
        {
            SecretKey = "INTEGRATION-TEST-SECRET-KEY-0123456789-abcdef",
            Issuer = "PanshiTest", Audience = "Panshi.Web"
        });
}

/// <summary>集测基类：绑定 fixture + 每测试独立操作者作用域。</summary>
[Collection("pg")]
public abstract class PgTestBase
{
    protected PgTestBase(PgFixture fx)
    {
        Fx = fx;
        Db = fx.Db;
    }

    protected PgFixture Fx { get; }
    protected ISqlSugarClient Db { get; }

    protected IDisposable As(long userId, string userName, long? deptId)
        => OperationUser.Use(new OperationContext(userId, userName, deptId));
}

[CollectionDefinition("pg")]
public class PgCollection : ICollectionFixture<PgFixture>
{
}
