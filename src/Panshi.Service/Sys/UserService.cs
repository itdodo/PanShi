using System.Linq.Expressions;
using MiniExcelLibs;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Common.Security;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using SqlSugar;

namespace Panshi.Service.Sys;

/// <summary>
/// 用户服务。防提权四防护（蓝图§5.3，建/导/改/分配全路径）：
/// ①重置 admin 密码→拒绝（本人走改密接口验旧密码）；②授予 admin 角色→仅内置管理员本人；
/// ③修改 admin 账号的角色→仅限本人；④停用/删除 admin 账号→拒绝。
/// </summary>
public class UserService(
    IRepository<SysUser> userRepo,
    IRepository<SysUserRole> userRoleRepo,
    IRepository<SysUserPosition> userPosRepo,
    IRepository<SysRole> roleRepo,
    IRepository<SysDept> deptRepo,
    ConfigService config,
    DataScopeService dataScope) : BaseService<SysUser>(userRepo)
{
    public const string AdminName = AuthService.AdminUserName;

    // ---------------- 查询 ----------------
    public async Task<PagedResult<UserDto>> PageAsync(UserQuery query, long actorId)
    {
        var ctx = await dataScope.ResolveAsync(actorId);
        var deptIds = query.DeptId is long d && query.DeptWithChildren ? await DescendantsAsync(d) : null;
        var keyword = query.Keyword?.Trim();

        // 红线 #1：条件一律 Expressionable，表达式体内零方法调用/三元
        var exp = Expressionable.Create<SysUser>();
        if (!string.IsNullOrEmpty(keyword)) exp.And(u => u.UserName!.Contains(keyword) || u.NickName.Contains(keyword));
        if (query.Status is EnableStatus st) exp.And(u => u.Status == st);
        if (deptIds is not null)
        {
            var deptIdList = deptIds;
            exp.And(u => u.DeptId != null && deptIdList.Contains(u.DeptId.Value));
        }
        else if (query.DeptId is long qd) exp.And(u => u.DeptId == qd);

        var (col, desc) = query.ResolveSort(UserSortWhitelist);
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);

        // ctx 非空时按最宽档后置过滤（分页总数保持库侧值）
        var rows = ctx is null ? page.Rows : page.Rows.Where(u => DataScopeService.UserInScope(ctx, u)).ToList();

        var deptNames = (await deptRepo.ListAsync()).ToDictionary(x => x.Id, x => x.DeptName);
        var ids = rows.Select(u => u.Id).ToHashSet();
        var roleLinks = (await userRoleRepo.ListAsync()).Where(l => ids.Contains(l.UserId))
            .GroupBy(l => l.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).ToList());
        var posLinks = (await userPosRepo.ListAsync()).Where(l => ids.Contains(l.UserId))
            .GroupBy(l => l.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.PositionId).ToList());

        return new PagedResult<UserDto>
        {
            Total = page.Total,
            Rows = rows.Select(u => ToDto(u, deptNames, roleLinks, posLinks)).ToList()
        };
    }

    public static readonly Dictionary<string, string> UserSortWhitelist = new()
    {
        ["createTime"] = "create_time", ["userName"] = "user_name", ["nickName"] = "nick_name",
        ["status"] = "status", ["lastLoginTime"] = "last_login_time"
    };

    public async Task<UserDto> GetAsync(long id)
    {
        var u = await Repo.GetAsync(id);
        var deptNames = new Dictionary<long, string>();
        if (u.DeptId is long d)
        {
            var dept = await deptRepo.FindAsync(d);
            if (dept is not null) deptNames[d] = dept.DeptName;
        }

        var roles = (await userRoleRepo.ListAsync(l => l.UserId == id)).Select(l => l.RoleId).ToList();
        var posts = (await userPosRepo.ListAsync(l => l.UserId == id)).Select(l => l.PositionId).ToList();
        return ToDto(u, deptNames,
            new Dictionary<long, List<long>> { [id] = roles },
            new Dictionary<long, List<long>> { [id] = posts });
    }

    /// <summary>用户下拉（正常状态；审批人自选等场景）。</summary>
    public async Task<List<OptionDto>> OptionsAsync()
        => (await userRepo.ListAsync(u => u.Status == EnableStatus.Enabled))
            .Select(u => new OptionDto { Value = u.Id.ToString(), Label = $"{u.NickName}（{u.UserName}）" })
            .ToList();

    // ---------------- 写入 ----------------
    public async Task<UserDto> CreateAsync(UserCreateDto dto, UserAuth actor)
    {
        if (await userRepo.ExistsAsync(u => u.UserName == dto.UserName))
            throw new BizException("登录名已存在");
        await GuardGrantAdminRoleAsync(dto.RoleIds, actor);

        var initPwd = string.IsNullOrEmpty(dto.Password)
            ? await config.GetAsync("sys.user.initPassword", "Abc@123456")
            : dto.Password;
        var (ok, msg) = PasswordHasher.ValidatePolicy(initPwd);
        if (!ok) throw new BizException(msg);

        var id = SnowflakeId.NextId();
        var user = new SysUser
        {
            Id = id, OwnerUserId = id,
            UserName = dto.UserName.Trim(), NickName = dto.NickName, Phone = dto.Phone, Email = dto.Email,
            DeptId = dto.DeptId, Status = dto.Status, Remark = dto.Remark,
            Password = PasswordHasher.Hash(initPwd), PwdUpdateTime = DateTime.Now,
            // 用共享初始密码创建 → 首登强制改密；管理员显式指定密码则不强制
            MustChangePassword = string.IsNullOrEmpty(dto.Password)
        };

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.InsertAsync(user);
            await ReplaceLinksAsync(id, dto.RoleIds, dto.PositionIds);
        });
        PermissionService.InvalidateAll();
        return await GetAsync(id);
    }

    public async Task UpdateAsync(long id, UserUpdateDto dto, UserAuth actor)
    {
        var user = await Repo.GetAsync(id);
        if (user.UserName == AdminName)
        {
            if (dto.Status == EnableStatus.Disabled) throw new BizException("不允许停用内置管理员账号");
            GuardAdminRolesWrite(user, dto.RoleIds, actor); // 防护③
        }

        await GuardGrantAdminRoleAsync(dto.RoleIds, actor);

        user.NickName = dto.NickName;
        user.Phone = dto.Phone;
        user.Email = dto.Email;
        user.DeptId = dto.DeptId;
        user.Status = dto.Status;
        user.Remark = dto.Remark;
        user.Version = dto.Version; // 红线 #6：乐观锁字段显式核对

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await UpdateWithConcurrencyCheckAsync(user);
            await ReplaceLinksAsync(id, dto.RoleIds, dto.PositionIds);
        });
        PermissionService.InvalidateAll();
    }

    public async Task DeleteAsync(long id, UserAuth actor)
    {
        var user = await Repo.GetAsync(id);
        if (user.UserName == AdminName) throw new BizException("不允许删除内置管理员账号");
        if (id == actor.UserId) throw new BizException("不能删除自己");

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.SoftDeleteAsync(id);
            await userRoleRepo.DeleteAsync(l => l.UserId == id);
            await userPosRepo.DeleteAsync(l => l.UserId == id);
        });
        PermissionService.InvalidateAll();
    }

    /// <summary>重置密码（防护①：admin 账号一律拒绝）。返回使用的初始密码。</summary>
    public async Task<string> ResetPasswordAsync(long id)
    {
        var user = await Repo.GetAsync(id);
        if (user.UserName == AdminName)
            throw new BizException("内置管理员请通过「修改密码」自助改密（需验证旧密码）");

        var pwd = await config.GetAsync("sys.user.initPassword", "Abc@123456");
        user.Password = PasswordHasher.Hash(pwd);
        user.PwdUpdateTime = DateTime.Now;
        user.MustChangePassword = true; // 重置为共享初始密码 → 强制该用户下次登录改密
        await Repo.UpdateColumnsAsync(user, "Password", "PwdUpdateTime", "MustChangePassword");
        return pwd;
    }

    public async Task AssignRolesAsync(long id, AssignRolesDto dto, UserAuth actor)
    {
        var user = await Repo.GetAsync(id);
        if (user.UserName == AdminName) GuardAdminRolesWrite(user, dto.RoleIds, actor);
        await GuardGrantAdminRoleAsync(dto.RoleIds, actor);

        await userRoleRepo.DeleteAsync(l => l.UserId == id);
        await ReplaceRolesAsync(id, dto.RoleIds);
        PermissionService.InvalidateAll();
    }

    // ---------------- 导入导出 ----------------
    public async Task<byte[]> TemplateBytesAsync()
    {
        using var ms = new MemoryStream();
        await ms.SaveAsAsync(new[]
        {
Array.Empty<UserImportRow>()
        });
        return ms.ToArray();
    }

    public async Task<ImportResultDto> ImportAsync(Stream stream, UserAuth actor)
    {
        // 模板不含角色列：导入路径天然不触碰防护②
        var rows = stream.Query<UserImportRow>()
            .Where(r => !string.IsNullOrWhiteSpace(r.UserName)).ToList();
        var depts = (await deptRepo.ListAsync()).GroupBy(d => d.DeptCode).ToDictionary(g => g.Key, g => g.First().Id);
        var exists = (await userRepo.ListAsync()).Select(u => u.UserName).ToHashSet();
        var initPwd = await config.GetAsync("sys.user.initPassword", "Abc@123456");

        var result = new ImportResultDto { Total = rows.Count };
        var batch = new List<SysUser>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 2;
            if (exists.Contains(row.UserName.Trim()))
            {
                result.Errors.Add($"第{line}行：登录名 {row.UserName} 已存在");
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.NickName))
            {
                result.Errors.Add($"第{line}行：姓名必填");
                continue;
            }

            long? deptId = null;
            if (!string.IsNullOrWhiteSpace(row.DeptCode))
            {
                if (!depts.TryGetValue(row.DeptCode.Trim(), out var did))
                {
                    result.Errors.Add($"第{line}行：部门编码不存在 {row.DeptCode}");
                    continue;
                }

                deptId = did;
            }
            var id = SnowflakeId.NextId(); // 红线 #8：批量直连显式填充
            batch.Add(new SysUser
            {
                Id = id, OwnerUserId = id,
                UserName = row.UserName.Trim(), NickName = row.NickName.Trim(), Phone = row.Phone,
                Email = row.Email, DeptId = deptId,
                Status = row.Status?.Trim() == "停用" ? EnableStatus.Disabled : EnableStatus.Enabled,
                Password = PasswordHasher.Hash(initPwd), PwdUpdateTime = DateTime.Now,
                MustChangePassword = true // 导入统一用初始密码 → 首登强制改密
            });
            exists.Add(row.UserName.Trim());
        }

        if (batch.Count > 0) await Repo.InsertRangeAsync(batch);
        result.Success = batch.Count;
        PermissionService.InvalidateAll();
        return result;
    }

    public async Task<byte[]> ExportBytesAsync(UserQuery query, long actorId)
    {
        query.PageNum = 1;
        query.PageSize = 200;
        var page = await PageAsync(query, actorId);
        var deptCodes = (await deptRepo.ListAsync()).ToDictionary(d => d.Id, d => d.DeptCode);
        var rows = page.Rows.Select(u => new UserImportRow
        {
            UserName = u.UserName, NickName = u.NickName, Phone = u.Phone, Email = u.Email,
            DeptCode = u.DeptId is not null && long.TryParse(u.DeptId, out var dd) ? deptCodes.GetValueOrDefault(dd) : null,
            Status = u.Status == EnableStatus.Disabled ? "停用" : "正常"
        }).ToList();
        using var ms = new MemoryStream();
        await ms.SaveAsAsync(rows);
        return ms.ToArray();
    }

    // ---------------- 防护与内部 ----------------
    private async Task GuardGrantAdminRoleAsync(List<long> roleIds, UserAuth actor)
    {
        if (roleIds.Count == 0 || actor.UserName == AdminName) return;
        var adminRole = await roleRepo.FindAsync(r => r.RoleCode == PermissionService.SuperRoleCode);
        if (adminRole is not null && roleIds.Contains(adminRole.Id))
            throw new BizException("仅内置管理员本人可授予 admin 角色"); // 防护②
    }

    private static void GuardAdminRolesWrite(SysUser target, List<long> roleIds, UserAuth actor)
    {
        if (actor.UserName != target.UserName)
            throw new BizException("仅内置管理员本人可调整其角色"); // 防护③
    }

    private async Task ReplaceLinksAsync(long userId, List<long> roleIds, List<long> positionIds)
    {
        await userRoleRepo.DeleteAsync(l => l.UserId == userId);
        await userPosRepo.DeleteAsync(l => l.UserId == userId);
        await ReplaceRolesAsync(userId, roleIds);
        if (positionIds.Count > 0)
            await userPosRepo.InsertRangeAsync(positionIds.Distinct()
                .Select(pid => new SysUserPosition { UserId = userId, PositionId = pid }).ToList());
    }

    private async Task ReplaceRolesAsync(long userId, List<long> roleIds)
    {
        if (roleIds.Count == 0) return;
        await userRoleRepo.InsertRangeAsync(roleIds.Distinct()
            .Select(rid => new SysUserRole { UserId = userId, RoleId = rid }).ToList());
    }

    private async Task<List<long>> DescendantsAsync(long deptId)
    {
        var all = await deptRepo.ListAsync();
        var result = new List<long> { deptId };
        var frontier = new List<long> { deptId };
        while (frontier.Count > 0)
        {
            var parents = frontier;
            frontier = all.Where(x => x.ParentId is long p && parents.Contains(p)).Select(x => x.Id)
                .Except(result).ToList();
            result.AddRange(frontier);
        }

        return result;
    }

    /// <summary>⚠️ 手工映射：新增乐观锁/审计字段必须逐列核对（红线 #6）。</summary>
    private static UserDto ToDto(SysUser u, IReadOnlyDictionary<long, string> deptNames,
        IReadOnlyDictionary<long, List<long>> roleLinks, IReadOnlyDictionary<long, List<long>> posLinks) => new()
    {
        Id = u.Id.ToString(), UserName = u.UserName, NickName = u.NickName, Phone = u.Phone, Email = u.Email,
        DeptId = u.DeptId?.ToString(),
        DeptName = u.DeptId is long d ? deptNames.GetValueOrDefault(d) : null,
        Status = u.Status, AvatarFileId = u.AvatarFileId?.ToString(), Remark = u.Remark,
        CreateTime = u.CreateTime, PwdUpdateTime = u.PwdUpdateTime, LastLoginTime = u.LastLoginTime,
        RoleIds = roleLinks.GetValueOrDefault(u.Id, []).Select(r => r.ToString()).ToList(),
        PositionIds = posLinks.GetValueOrDefault(u.Id, []).Select(r => r.ToString()).ToList(),
        Version = u.Version
    };
}
