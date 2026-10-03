using Microsoft.Extensions.Diagnostics.HealthChecks;
using SqlSugar;

namespace Panshi.Api.Middleware;

/// <summary>
/// 就绪探针：真的问一次数据库。挂到 <c>/api/v1/health/ready</c>（带 ready 标签），
/// 而 <c>/api/v1/health</c> 是存活探针、不查依赖——把「库抖一下」做成容器 unhealthy
/// 会让 API 被反复判死重启，而它自己的启动等待已经能把库等起来。
/// </summary>
public class DbHealthCheck(ISqlSugarClient db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            db.Ado.CommandTimeOut = 3; // 库被锁死时不能把探针也拖住
            await db.Ado.SqlQueryAsync<int>("select 1");
            return HealthCheckResult.Healthy("数据库可应答");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"数据库不可用：{ex.Message}");
        }
    }
}
