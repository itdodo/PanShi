using Panshi.Common.Exceptions;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>
/// 事务助手（红线 #3/#4）：
/// UseTran 不可嵌套——检测到环境事务则直接复用；
/// DbResult 必须检查 IsSuccess 并重抛 ErrorException（否则异常被吞、业务静默失败）。
/// </summary>
public static class Tran
{
    public static async Task<T> RunAsync<T>(ISqlSugarClient db, Func<Task<T>> action)
    {
        if (db.Ado.Transaction is not null) return await action();

        var result = await db.Ado.UseTranAsync(action);
        if (!result.IsSuccess)
        {
            // 红线 #4：必须重抛 ErrorException 且保持原类型（BizException 409/400 语义不能被包装成 500）
            if (result.ErrorException is not null) throw result.ErrorException;
            throw new InvalidOperationException($"事务执行失败：{result.ErrorMessage}");
        }

        return result.Data;
    }

    public static async Task RunAsync(ISqlSugarClient db, Func<Task> action)
        => await RunAsync(db, async () =>
        {
            await action();
            return true;
        });
}
