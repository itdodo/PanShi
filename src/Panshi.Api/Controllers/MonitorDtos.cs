using System.Text.Json.Serialization;
using Panshi.Common.Json;

namespace Panshi.Api.Controllers;

/*
 * 监控页的响应形状。放在 Api 层而不是 Model/Dtos：字段要标 LongAsNumberConverter，
 * 而 Model 层刻意不依赖 Common（分层约束），引不到那个转换器。
 */

/// <summary>
/// 指标快照（<c>GET /api/v1/monitor/metrics</c>）。
/// ⚠️ 计数一律标 <see cref="LongAsNumberConverter"/>：全局那条 long→string 是给雪花 id 用的
/// （JS number 装不下 64 位），指标被写成字符串就没法直接聚合，等于自废。
/// </summary>
public class MetricsDto
{
    public string MachineName { get; set; } = "";
    public double UptimeMin { get; set; }
    public RequestCountersDto Requests { get; set; } = new();
    public GcStatsDto Gc { get; set; } = new();
    public ThreadStatsDto Threads { get; set; } = new();
    public DbStatsDto Db { get; set; } = new();
}

public class RequestCountersDto
{
    [JsonConverter(typeof(LongAsNumberConverter))] public long Total { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long Active { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long ServerErrors { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long ClientErrors { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long Unauthorized { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long Forbidden { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long Conflict { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long Throttled { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long AvgMs { get; set; }
    [JsonConverter(typeof(LongAsNumberConverter))] public long MaxMs { get; set; }
}

public class GcStatsDto
{
    public bool IsServer { get; set; }
    public int Gen0 { get; set; }
    public int Gen1 { get; set; }
    public int Gen2 { get; set; }
    public double HeapMb { get; set; }
    public double CommittedMb { get; set; }
    /// <summary>自进程启动以来的 GC 停顿占比（%），不是区间值——只看趋势。</summary>
    public double PausePercent { get; set; }
}

public class ThreadStatsDto
{
    public int WorkerBusy { get; set; }
    public int WorkerMax { get; set; }
    public int IoBusy { get; set; }
    public int IoMax { get; set; }
    /// <summary>OS 线程数（含 GC 最终器与 Hangfire worker）。</summary>
    public int OsThreads { get; set; }
}

public class DbStatsDto
{
    /// <summary>本库当前的 PG 后端数（含空闲连接）。逼近连接池上限时 requests.active 也会一起抬高。</summary>
    public int ActiveConnections { get; set; }
}
