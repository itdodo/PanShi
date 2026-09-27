using Panshi.Common.Exceptions;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// P0 横向越权回归：列表已按数据权限过滤，「按 id 直读单条」的入口必须同一判定。
/// 修前这些详情端点只 [Authorize]，任何登录用户换个 id 就能读到别人的单据/资料/附件/审批意见。
/// </summary>
[Collection("pg")]
public class ObjectAccessTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => "oa" + SnowflakeId.NextId();

    private async Task<SysUser> MakeUserAsync(string name, DataScopeType scope, long deptId)
    {
        var user = new SysUser
        {
            Id = SnowflakeId.NextId(), UserName = name, NickName = "越权回归", DeptId = deptId,
            Password = "not-used", Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now
        };
        user.OwnerUserId = user.Id;
        var role = new SysRole
        {
            Id = SnowflakeId.NextId(), RoleCode = "r_" + name, RoleName = "越权回归角色",
            DataScope = scope, Status = EnableStatus.Enabled, Sort = 999
        };
        await Db.Insertable(user).ExecuteCommandAsync();
        await Db.Insertable(role).ExecuteCommandAsync();
        await Db.Insertable(new SysUserRole { Id = SnowflakeId.NextId(), UserId = user.Id, RoleId = role.Id })
            .ExecuteCommandAsync();
        return user;
    }

    private async Task CleanAsync(params string[] names)
    {
        foreach (var n in names)
        {
            var ids = await Db.Queryable<SysUser>().Where(u => u.UserName == n).Select(u => u.Id).ToListAsync();
            if (ids.Count > 0)
                await Db.Deleteable<SysUserRole>().Where(l => ids.Contains(l.UserId)).ExecuteCommandAsync();
        }
        await Db.Deleteable<SysUser>().Where(u => names.Contains(u.UserName)).ExecuteCommandAsync();
        await Db.Deleteable<SysRole>().Where(r => r.RoleCode.StartsWith("r_oa")).ExecuteCommandAsync();
        await Db.Deleteable<BizExpense>().Where(x => x.Reason != null && x.Reason.StartsWith("越权回归")).ExecuteCommandAsync();
        await Db.Deleteable<SysFile>().Where(f => f.FileName == "附件.bin").ExecuteCommandAsync();
        await Db.Deleteable<SysFlowTask>().Where(t => t.NodeCode == "oa-n1").ExecuteCommandAsync();
        await Db.Deleteable<SysFlowInstance>().Where(i => i.FlowCode == "oa-none").ExecuteCommandAsync();
    }

    [Fact]
    public async Task Expense_Detail_Requires_Scope_Visibility()
    {
        var tag = Tag();
        var me = await MakeUserAsync("oa_" + tag + "a", DataScopeType.Self, 3);
        var other = await MakeUserAsync("oa_" + tag + "b", DataScopeType.Self, 4);
        var doc = new BizExpense
        {
            DocNo = "BX_OA_" + tag, OwnerUserId = other.Id, OwnerUserName = other.UserName,
            DeptId = other.DeptId, Amount = 66m, Reason = "越权回归单据", Status = BizDocStatus.Draft
        };
        await Db.Insertable(doc).ExecuteCommandAsync();
        try
        {
            var ex = await Record.ExceptionAsync(() => Fx.Expenses().GetAsync(doc.Id, me.Id));
            Assert.IsType<BizException>(ex);
            Assert.Equal(403, ((BizException)ex!).Code);

            var mine = await Fx.Expenses().GetAsync(doc.Id, other.Id);
            Assert.Equal(doc.Id.ToString(), mine.Id);
        }
        finally
        {
            await CleanAsync(me.UserName, other.UserName);
        }
    }

    [Fact]
    public async Task User_Detail_Requires_Scope_Visibility()
    {
        var tag = Tag();
        var me = await MakeUserAsync("oa_" + tag + "a", DataScopeType.Self, 3);
        var other = await MakeUserAsync("oa_" + tag + "b", DataScopeType.Self, 4);
        try
        {
            var ex = await Record.ExceptionAsync(() => Fx.UserService().GetAsync(other.Id, me.Id));
            Assert.IsType<BizException>(ex);
            Assert.Equal(403, ((BizException)ex!).Code);

            var self = await Fx.UserService().GetAsync(me.Id, me.Id);
            Assert.Equal(me.UserName, self.UserName);
        }
        finally
        {
            await CleanAsync(me.UserName, other.UserName);
        }
    }

    [Fact]
    public async Task File_Access_Blocks_OtherUsers_But_Allows_Avatars_And_Visible_Refs()
    {
        var tag = Tag();
        var me = await MakeUserAsync("oa_" + tag + "a", DataScopeType.Self, 3);
        var other = await MakeUserAsync("oa_" + tag + "b", DataScopeType.Self, 4);
        async Task<SysFile> Upload(string? bizType, long uploaderId)
        {
            var f = new SysFile
            {
                Id = SnowflakeId.NextId(), StoreName = "oa-" + SnowflakeId.NextId() + ".bin",
                FileName = "附件.bin", ContentType = "application/octet-stream", Size = 8,
                BizType = bizType, UploaderId = uploaderId
            };
            await Db.Insertable(f).ExecuteCommandAsync();
            return f;
        }
        try
        {
            var access = Fx.FileAccess();
            var secret = await Upload("doc", other.Id);

            var ex = await Record.ExceptionAsync(() => access.EnsureReadableAsync(secret.Id, me.Id));
            Assert.IsType<BizException>(ex);
            Assert.Equal(403, ((BizException)ex!).Code);

            // 自己上传的能读；头像同事也能读
            await access.EnsureReadableAsync((await Upload("doc", me.Id)).Id, me.Id);
            await access.EnsureReadableAsync((await Upload("avatar", other.Id)).Id, me.Id);

            // 附件被一张我看得见的单据引用 → 能读
            var mine = new BizExpense
            {
                DocNo = "BX_OA_" + tag + "r", OwnerUserId = me.Id, OwnerUserName = me.UserName,
                DeptId = me.DeptId, Amount = 12m, Reason = "越权回归引用", Status = BizDocStatus.Draft,
                AttachmentIds = $"[\"{secret.Id}\"]"
            };
            await Db.Insertable(mine).ExecuteCommandAsync();
            await access.EnsureReadableAsync(secret.Id, me.Id);
        }
        finally
        {
            await CleanAsync(me.UserName, other.UserName);
        }
    }

    [Fact]
    public async Task Flow_Instance_Detail_Only_For_Participants()
    {
        var tag = Tag();
        var me = await MakeUserAsync("oa_" + tag + "a", DataScopeType.Self, 3);
        var submitter = await MakeUserAsync("oa_" + tag + "b", DataScopeType.Self, 4);
        var instance = new SysFlowInstance
        {
            Id = SnowflakeId.NextId(), DefinitionId = 1, FlowCode = "oa-none",
            BusinessTable = "biz_expense", BusinessId = 1, Summary = "越权回归实例",
            SubmitterId = submitter.Id, SubmitterName = submitter.UserName,
            Status = FlowInstanceStatus.Running, CreateTime = DateTime.Now
        };
        await Db.Insertable(instance).ExecuteCommandAsync();
        try
        {
            var query = Fx.FlowQuery();
            var ex = await Record.ExceptionAsync(() => query.DetailAsync(instance.Id, me.Id));
            Assert.IsType<BizException>(ex);
            Assert.Equal(403, ((BizException)ex!).Code);

            var ok = await query.DetailAsync(instance.Id, submitter.Id);
            Assert.Equal(instance.Id.ToString(), ok.Instance.Id);

            // 任务挂到 me 名下后，me 作为审批人也可读
            await Db.Insertable(new SysFlowTask
            {
                Id = SnowflakeId.NextId(), InstanceId = instance.Id, NodeCode = "oa-n1", NodeName = "审批",
                ApproverUserId = me.Id, ApproverName = me.UserName, Status = FlowTaskStatus.Pending,
                CreateTime = DateTime.Now
            }).ExecuteCommandAsync();
            var asApprover = await query.DetailAsync(instance.Id, me.Id);
            Assert.Equal(instance.Id.ToString(), asApprover.Instance.Id);
        }
        finally
        {
            await CleanAsync(me.UserName, submitter.UserName);
        }
    }
}
