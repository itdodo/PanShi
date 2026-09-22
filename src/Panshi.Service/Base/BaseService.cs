using System.Linq.Expressions;
using Panshi.Common.Exceptions;
using Panshi.Model.Entities;
using Panshi.Repository;

namespace Panshi.Service.Base;

/// <summary>
/// 服务基类。⚠️ 红线 #6：手工 DTO 映射新增乐观锁字段必须逐一核对；
/// 编辑统一入口 = UpdateWithConcurrencyCheckAsync（乐观锁 + 字段审计）。
/// </summary>
public abstract class BaseService<T>(IRepository<T> repo) where T : BaseEntity, new()
{
    protected IRepository<T> Repo { get; } = repo;

    /// <summary>不存在即抛 NotFound。</summary>
    public virtual async Task<T> GetRequiredAsync(long id) => await Repo.GetAsync(id);

    /// <summary>
    /// 编辑统一入口：entity.Version 已由 DTO 映射赋值（乐观锁期望版本）；
    /// 冲突抛 409「数据已被他人修改，请刷新后重试」。
    /// </summary>
    public Task UpdateWithConcurrencyCheckAsync(T entity)
        => Repo.UpdateWithAuditAsync(entity, entity.Version);

    /// <summary>表达式条件拼装辅助（红线 #1：查询表达式内禁止宿主变量方法调用，统一走这里）。</summary>
    protected static Expression<Func<T, bool>> And(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
        => Compile(a, b, true);

    protected static Expression<Func<T, bool>> Or(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
        => Compile(a, b, false);

    private static Expression<Func<T, bool>> Compile(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b,
        bool andAlso)
    {
        var p = a.Parameters[0];
        var body = andAlso
            ? Expression.AndAlso(a.Body, Expression.Invoke(b, p))
            : Expression.OrElse(a.Body, Expression.Invoke(b, p));
        return Expression.Lambda<Func<T, bool>>(body, p);
    }
}
