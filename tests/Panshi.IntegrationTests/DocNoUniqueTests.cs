using Panshi.Model.Entities;
using SqlSugar;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 单据表「存活行单号唯一」的数据库兜底（迁移 0013 补的就是这条）。
/// 发号器 0012 已经改成原子取号，但这道底线此前只有 scm 三张单据表有，biz 两张没有——
/// 压测那天就在 biz_expense 里撞出过两行 BX20260928001（旧 COUNT 发号会把已删单子的号重发）。
/// ① 目录级不变量：库里带 doc_no 列的表必须正好是「五张单据表 + 流水表」这一组，
///    五张单据表都必须有 uk_&lt;表名&gt;_doc_no 且谓词带 is_deleted = false。
///    用目录扫而不是写死五个名字，是为了以后新增第六张单据表时当场红，而不是等下一次撞号才发现；
///     scm_stock_ledger 的 doc_no 是「这笔流水来自哪张单」，一单多行天然重复，所以它单独列出、不参与断言。
/// ② 真插一条重复号确认被 23505 拒；并确认软删行不挡同号重用——部分索引的这层语义，
///    正是历史重号不必清数据也能建索引的原因。
/// 注：这里刻意直连仓储而不是走服务层，因为服务层永远发新号，撞不了号；要测的就是数据库自己挡不挡。
/// </summary>
[Collection("pg")]
public class DocNoUniqueTests(PgFixture fx) : PgTestBase(fx)
{
    /// <summary>单据表：单号是「这张单」的身份证，存活行里必须唯一</summary>
    private static readonly string[] DocumentTables =
    [
        "biz_expense", "biz_purchase_request", "scm_purchase_order", "scm_sales_order", "scm_stock_doc"
    ];

    /// <summary>有 doc_no 列但不是单据表：这列存的是来源单号（外键性质），允许重复</summary>
    private static readonly string[] ReferenceOnlyTables = ["scm_stock_ledger"];

    private static string DocNo(string prefix) => $"{prefix}{DateTime.Now:yyyyMMdd}9{Random.Shared.Next(1000, 9999)}";

    [Fact]
    public async Task Every_Doc_Table_Guards_Live_Doc_No_With_Partial_Unique_Index()
    {
        var withDocNo = await Db.Ado.SqlQueryAsync<string>(
            "select table_name from information_schema.columns "
            + "where table_schema='public' and column_name='doc_no' order by table_name");

        // 先钉住「谁有 doc_no 列」这个集合本身：多了表就逼做一次判断（是单据表就建索引，
        // 是流水/引用列就加进 ReferenceOnlyTables），少了列说明有人改名，也不该悄悄放过。
        // 两边都按 Ordinal 排，免得 PG 的库排序规则与 .NET 对下划线的处理不一致造成假失败
        var actual = withDocNo.OrderBy(t => t, StringComparer.Ordinal).ToArray();
        var expected = DocumentTables.Concat(ReferenceOnlyTables).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);

        foreach (var table in DocumentTables)
        {
            var def = await Db.Ado.SqlQueryAsync<string>(
                "select indexdef from pg_indexes where schemaname='public' and tablename=@t and indexname=@n",
                new { t = table, n = $"uk_{table}_doc_no" });
            Assert.True(def.Count == 1, $"{table} 缺 uk_{table}_doc_no（存活行单号唯一的数据库兜底）");
            Assert.Contains("UNIQUE", def[0]);
            // 谓词漏了 is_deleted = false 就等于「软删过的号永远不能再发」，与既有四张口径不一致
            Assert.Contains("is_deleted = false", def[0]);
        }
    }

    [Fact]
    public async Task Expense_Duplicate_Live_Doc_No_Is_Rejected_And_Reusable_After_Soft_Delete()
    {
        var repo = Fx.Repo<BizExpense>();
        var docNo = DocNo("BX");
        // 插一行就登记一行：上一版把「重复插」写在 lambda 里、id 没被收集，摘掉索引跑红一次就在库里漏了一行
        var ids = new List<long>();

        async Task<long> Insert(string name, decimal amount)
        {
            var row = await repo.InsertAsync(new BizExpense { DocNo = docNo, OwnerUserName = name, Amount = amount });
            ids.Add(row.Id);
            return row.Id;
        }

        try
        {
            var first = await Insert("单号兜底用例", 12.5m);

            var ex = await Record.ExceptionAsync(() => Insert("单号兜底用例", 60m));
            Assert.NotNull(ex);
            Assert.Contains("uk_biz_expense_doc_no", ex!.Message);

            // 软删 → 同号可以重新占用（部分索引只约束存活行）
            Assert.True(await repo.SoftDeleteAsync(first));
            var again = await Insert("单号兜底用例-复用", 30m);
            Assert.NotEqual(first, again);
        }
        finally
        {
            if (ids.Count > 0) await Db.Deleteable<BizExpense>().In(ids).ExecuteCommandAsync();
        }
    }

    [Fact]
    public async Task Purchase_Request_Duplicate_Live_Doc_No_Is_Rejected()
    {
        var repo = Fx.Repo<BizPurchaseRequest>();
        var docNo = DocNo("CG");
        var ids = new List<long>();

        async Task Insert(int quantity, decimal amount)
        {
            var row = await repo.InsertAsync(new BizPurchaseRequest
            {
                DocNo = docNo, OwnerUserName = "单号兜底用例", ItemName = "回归物料", Quantity = quantity, Amount = amount
            });
            ids.Add(row.Id);
        }

        try
        {
            await Insert(1, 10m);

            var ex = await Record.ExceptionAsync(() => Insert(2, 20m));
            Assert.NotNull(ex);
            Assert.Contains("uk_biz_purchase_request_doc_no", ex!.Message);
        }
        finally
        {
            if (ids.Count > 0) await Db.Deleteable<BizPurchaseRequest>().In(ids).ExecuteCommandAsync();
        }
    }
}
