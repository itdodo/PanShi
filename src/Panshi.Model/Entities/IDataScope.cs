namespace Panshi.Model.Entities;

/// <summary>
/// 数据权限标记接口：实现者自动被 Sys 数据权限过滤器接管（业务零侵入）。
/// DeptId=归属部门；OwnerUserId=归属人（「仅本人」档比对）。
/// </summary>
public interface IDataScope
{
    long? DeptId { get; set; }

    long? OwnerUserId { get; set; }
}
