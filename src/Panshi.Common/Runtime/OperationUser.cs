namespace Panshi.Common.Runtime;

/// <summary>当前请求的操作者上下文（由认证中间件写入，AOP/日志/审计读取）。</summary>
public sealed record OperationContext(long? UserId, string? UserName, long? DeptId);

/// <summary>
/// 环境态操作者：AsyncLocal 传递。SqlSugarScope 为单例，AOP 回调无法注入 Scoped 服务，
/// 统一从这里取当前用户（后台作业里可显式 Use(systemId)）。
/// </summary>
public static class OperationUser
{
    private static readonly AsyncLocal<OperationContext?> Current = new();

    public static OperationContext? Context => Current.Value;

    public static long? UserId => Current.Value?.UserId;

    public static string? UserName => Current.Value?.UserName;

    public static long? DeptId => Current.Value?.DeptId;

    public static IDisposable Use(OperationContext context)
    {
        var previous = Current.Value;
        Current.Value = context;
        return new Reverter(previous);
    }

    private sealed class Reverter(OperationContext? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
