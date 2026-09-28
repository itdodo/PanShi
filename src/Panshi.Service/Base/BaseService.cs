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

    /// <summary>
    /// 编辑统一入口：entity.Version 已由 DTO 映射赋值（乐观锁期望版本）；
    /// 冲突抛 409「数据已被他人修改，请刷新后重试」。
    /// </summary>
    public Task UpdateWithConcurrencyCheckAsync(T entity)
        => Repo.UpdateWithAuditAsync(entity, entity.Version);
}
