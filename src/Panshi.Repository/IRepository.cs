using System.Linq.Expressions;
using Panshi.Common.Results;
using Panshi.Model.Entities;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>泛型仓储（软删除全局过滤已生效；分页排序列必须来自白名单）。</summary>
public interface IRepository<T> where T : BaseEntity, new()
{
    ISqlSugarClient Db { get; }

    /// <summary>按主键查（无则 null）</summary>
    Task<T?> FindAsync(long id);

    /// <summary>按条件查首条（无则 null）</summary>
    Task<T?> FindAsync(Expression<Func<T, bool>> where);

    /// <summary>按主键查（无则抛 NotFound）</summary>
    Task<T> GetAsync(long id);

    Task<List<T>> ListAsync(Expression<Func<T, bool>>? where = null);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> where);

    Task<long> CountAsync(Expression<Func<T, bool>>? where = null);

    /// <summary>分页（⚠️ sortColumn 物理列名，已过白名单；默认 create_time desc——红线 #5）</summary>
    Task<PagedResult<T>> PageAsync(Expression<Func<T, bool>> where, int pageNum, int pageSize,
        string? sortColumn = null, bool desc = true);

    /// <summary>单条插入（AOP 自动填充雪花 Id/审计字段）</summary>
    Task<T> InsertAsync(T entity);

    /// <summary>批量插入（⚠️ 红线 #8：直连 Insertable 不触发 AOP，显式填充 Id/CreateTime）</summary>
    Task<int> InsertRangeAsync(List<T> entities);

    /// <summary>全列更新（不含乐观锁校验；审计字段 AOP 填充）</summary>
    Task<bool> UpdateAsync(T entity);

    /// <summary>仅更新指定列（表达式白名单，物理列名）</summary>
    Task<bool> UpdateColumnsAsync(T entity, params string[] camelFields);

    /// <summary>
    /// 乐观锁单条 SQL：SET 业务列+Version+1 WHERE Id AND Version（⚠️ 红线 #2：禁 Updateable.WhereColumns）。
    /// 0 行 = false（冲突）。
    /// </summary>
    Task<bool> UpdateWithVersionCheckAsync(T entity, int expectedVersion);

    /// <summary>编辑统一入口 = 乐观锁 + 字段级审计（冲突不落审计、无差异不落审计；冲突抛 409）。</summary>
    Task UpdateWithAuditAsync(T entity, int expectedVersion);

    /// <summary>软删除（置 IsDeleted；返回是否命中）</summary>
    Task<bool> SoftDeleteAsync(long id);

    Task<int> SoftDeleteAsync(Expression<Func<T, bool>> where);

    /// <summary>物理删除（日志/关联表清理用）</summary>
    Task<bool> DeleteAsync(long id);

    Task<int> DeleteAsync(Expression<Func<T, bool>> where);
}
