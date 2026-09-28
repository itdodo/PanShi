using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 部门树关键词搜索的层级回归。BuildTree 以 ParentId==null 为根递归，
/// 修复前 TreeAsync 先按关键词过滤再建树，深层命中会因为父级被筛掉而整棵消失（搜「财务」返回空数组），
/// 而前端 dept/index.vue 的注释与调用都假定后端「保留层级」。
/// </summary>
[Collection("pg")]
public class DeptTreeSearchTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => "dts" + SnowflakeId.NextId();

    [Fact]
    public async Task TreeAsync_Keyword_Keeps_Ancestor_Chain()
    {
        var tag = Tag();
        var chain = new List<SysDept>
        {
            New(null, "根_" + tag, 900),
            New(null, "中层_" + tag, 901),
            New(null, "叶子部门_" + tag, 902)
        };
        chain[1].ParentId = chain[0].Id;
        chain[2].ParentId = chain[1].Id;
        await Db.Insertable(chain).ExecuteCommandAsync();

        try
        {
            // 关键词只命中第三层，前两层名字都不含它
            var tree = await Fx.Depts().TreeAsync("叶子部门_" + tag);

            var root = Assert.Single(tree);
            Assert.Equal("根_" + tag, root.DeptName);

            var mid = Assert.Single(root.Children);
            Assert.Equal("中层_" + tag, mid.DeptName);

            var leaf = Assert.Single(mid.Children);
            Assert.Equal("叶子部门_" + tag, leaf.DeptName);
        }
        finally
        {
            await Db.Deleteable<SysDept>().In(chain.Select(d => d.Id).ToList()).ExecuteCommandAsync();
        }
    }

    [Fact]
    public async Task TreeAsync_Without_Keyword_Returns_Only_Roots_On_Top_Level()
    {
        var tree = await Fx.Depts().TreeAsync();
        Assert.NotEmpty(tree);
        Assert.All(tree, r => Assert.True(string.IsNullOrEmpty(r.ParentId)));
    }

    private static SysDept New(long? parent, string name, int sort) => new()
    {
        Id = SnowflakeId.NextId(), ParentId = parent, DeptCode = name, DeptName = name,
        Sort = sort, Status = (int)EnableStatus.Enabled, CreateTime = DateTime.Now
    };
}
