using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Panshi.Service.Md;
using SqlSugar;

namespace Panshi.Service.Scm;

/// <summary>
/// 库存单据与过账。库存的唯一入口：草稿对台账无影响，只有 Post 会在一个事务里
/// 改 scm_stock 并写 scm_stock_ledger；作废按该单当时的流水逐条反向冲销，
/// 这样「台账 = 流水累计和」这条不变量在任何单据类型下都成立。
/// </summary>
public class StockDocService(
    IRepository<ScmStockDoc> repo,
    IRepository<ScmStockDocLine> lineRepo,
    IRepository<ScmStock> stockRepo,
    IRepository<ScmStockLedger> ledgerRepo,
    IRepository<MdMaterial> materials,
    IRepository<MdWarehouse> warehouses,
    DataScopeService dataScope) : BaseService<ScmStockDoc>(repo)
{
    private static readonly Dictionary<StockDocKind, string> Prefixes = new()
    {
        [StockDocKind.PurchaseIn] = "RK",
        [StockDocKind.SalesOut] = "CK",
        [StockDocKind.OtherIn] = "QRK",
        [StockDocKind.OtherOut] = "QCK",
        [StockDocKind.Transfer] = "DB",
        [StockDocKind.Count] = "PD"
    };

    /// <summary>入库 +1、出库与调拨源仓 -1；盘点走「实盘 − 账面」所以是 0（目标仓另算 +1）。</summary>
    private static int SignOf(StockDocKind kind) => kind switch
    {
        StockDocKind.PurchaseIn or StockDocKind.OtherIn => 1,
        StockDocKind.SalesOut or StockDocKind.OtherOut or StockDocKind.Transfer => -1,
        _ => 0
    };

    public static string KindLabel(StockDocKind kind) => kind switch
    {
        StockDocKind.PurchaseIn => "采购入库",
        StockDocKind.SalesOut => "销售出库",
        StockDocKind.OtherIn => "其他入库",
        StockDocKind.OtherOut => "其他出库",
        StockDocKind.Transfer => "调拨",
        _ => "盘点"
    };

    /* --------------------------------- 查询 --------------------------------- */

    public async Task<PagedResult<StockDocDto>> PageAsync(StockDocQuery query, long userId)
    {
        var kw = query.Keyword?.Trim();
        var ctx = query.Mine ? null : await dataScope.ResolveAsync(userId);
        var exp = Expressionable.Create<ScmStockDoc>();
        if (query.Mine) exp.And(d => d.OwnerUserId == userId);
        else exp.And(DataScopeService.Filter<ScmStockDoc>(ctx));
        if (!string.IsNullOrEmpty(kw)) exp.And(d => d.DocNo.Contains(kw) || d.WarehouseName.Contains(kw));
        if (query.Kind is StockDocKind kind) exp.And(d => d.Kind == kind);
        if (query.Status is StockDocStatus status) exp.And(d => d.Status == status);
        if (long.TryParse(query.WarehouseId, out var warehouseId)) exp.And(d => d.WarehouseId == warehouseId);
        if (query.Begin is DateTime begin) exp.And(d => d.BizDate >= begin);
        if (query.End is DateTime end) exp.And(d => d.BizDate <= end);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        var rows = page.Rows.Select(ToDto).ToList();
        await FillLineCountsAsync(rows);
        return new PagedResult<StockDocDto> { Total = page.Total, Rows = rows };
    }

    public async Task<StockDocDto> GetAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        if (!DataScopeService.IsVisible(await dataScope.ResolveAsync(userId), doc))
            throw BizException.Forbidden("无权查看该单据");
        var dto = ToDto(doc);
        var lines = await lineRepo.ListAsync(l => l.DocId == id);
        dto.Lines = lines.Select(ToLineDto).ToList();
        if (doc.Kind == StockDocKind.Count)
        {
            // 盘点要看得到「差异是多少」，所以把当前账面数一并回给前端
            foreach (var line in dto.Lines)
                line.BookQty = await BookQtyAsync(doc.WarehouseId, long.Parse(line.MaterialId));
        }
        return dto;
    }

    /* --------------------------------- 写入 --------------------------------- */

    public async Task<StockDocDto> CreateAsync(StockDocSaveDto dto, long userId, string userName, long? deptId)
    {
        var (warehouse, target) = await ResolveWarehousesAsync(dto);
        var lines = await ResolveLinesAsync(dto);

        var doc = new ScmStockDoc
        {
            DocNo = await NextDocNoAsync(dto.Kind), Kind = dto.Kind,
            WarehouseId = warehouse.Id, WarehouseName = warehouse.WarehouseName,
            TargetWarehouseId = target?.Id, TargetWarehouseName = target?.WarehouseName,
            BizDate = dto.BizDate.Date, SourceOrderNo = Blank.ToNull(dto.SourceOrderNo),
            TotalQty = lines.Sum(l => l.Quantity), Remark = Blank.ToNull(dto.Remark),
            OwnerUserId = userId, OwnerUserName = userName, DeptId = deptId, Status = StockDocStatus.Draft
        };

        return await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.InsertAsync(doc);
            await InsertLinesAsync(doc.Id, dto, lines);
            return ToDto(doc);
        });
    }

    public async Task UpdateAsync(long id, StockDocSaveDto dto, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardDraft(doc, userId);
        var (warehouse, target) = await ResolveWarehousesAsync(dto);
        var lines = await ResolveLinesAsync(dto);

        doc.Kind = dto.Kind;
        doc.WarehouseId = warehouse.Id;
        doc.WarehouseName = warehouse.WarehouseName;
        doc.TargetWarehouseId = target?.Id;
        doc.TargetWarehouseName = target?.WarehouseName;
        doc.BizDate = dto.BizDate.Date;
        doc.SourceOrderNo = Blank.ToNull(dto.SourceOrderNo);
        doc.TotalQty = lines.Sum(l => l.Quantity);
        doc.Remark = Blank.ToNull(dto.Remark);
        doc.Version = dto.Version;

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await UpdateWithConcurrencyCheckAsync(doc);
            await lineRepo.DeleteAsync(l => l.DocId == id);
            await InsertLinesAsync(id, dto, lines);
        });
    }

    public async Task DeleteAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardDraft(doc, userId);
        await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.SoftDeleteAsync(id);
            await lineRepo.SoftDeleteAsync(l => l.DocId == id);
        });
    }

    /// <summary>过账：唯一会动库存的动作。任一行可用量不足就整单回滚。</summary>
    public async Task<StockDocDto> PostAsync(long id, long userId, string userName)
    {
        var doc = await Repo.GetAsync(id);
        GuardDraft(doc, userId);
        var lines = await lineRepo.ListAsync(l => l.DocId == id);
        if (lines.Count == 0) throw new BizException("明细为空，不能过账");

        var bizTime = DateTime.Now;
        await Tran.RunAsync(Repo.Db, async () =>
        {
            foreach (var line in lines)
            {
                var delta = doc.Kind switch
                {
                    StockDocKind.Count => line.Quantity - await BookQtyAsync(doc.WarehouseId, line.MaterialId),
                    _ => line.Quantity * SignOf(doc.Kind)
                };
                await MoveAsync(doc.WarehouseId, doc.WarehouseName, line, delta, doc, bizTime, userName);
                if (doc.Kind == StockDocKind.Transfer && doc.TargetWarehouseId is { } targetId)
                    await MoveAsync(targetId, doc.TargetWarehouseName ?? "", line, line.Quantity, doc, bizTime, userName);
            }

            doc.Status = StockDocStatus.Posted;
            doc.PostedTime = bizTime;
            await Repo.UpdateColumnsAsync(doc, "Status", "PostedTime");
        });

        return ToDto(doc);
    }

    /// <summary>
    /// 作废：按本单已记的流水逐条反向冲销，而不是按当前明细重算——
    /// 明细可能被改过、账面也可能已变，只有流水才是「当时真正动了多少」的凭据。
    /// </summary>
    public async Task<StockDocDto> VoidAsync(long id, long userId, string userName)
    {
        var doc = await Repo.GetAsync(id);
        if (doc.OwnerUserId != userId) throw BizException.Forbidden("只能操作本人单据");
        if (doc.Status != StockDocStatus.Posted) throw new BizException("只有已过账的单据才能作废");

        var entries = await ledgerRepo.ListAsync(l => l.DocId == id);
        if (entries.Count == 0) throw new BizException("该单据没有流水记录，无法作废");

        var bizTime = DateTime.Now;
        await Tran.RunAsync(Repo.Db, async () =>
        {
            foreach (var entry in entries.OrderBy(l => l.Id))
            {
                var line = new ResolvedStockLine(entry.MaterialId, entry.MaterialCode, entry.MaterialName, entry.Unit);
                await MoveAsync(entry.WarehouseId, entry.WarehouseName, line, -entry.ChangeQty, doc, bizTime, userName);
            }
            doc.Status = StockDocStatus.Void;
            await Repo.UpdateColumnsAsync(doc, "Status");
        });

        return ToDto(doc);
    }

    /* -------------------------------- 过账内核 -------------------------------- */

    /// <summary>过账与作废共用的「一次移动」描述：只带快照，变动量走 delta 参数。</summary>
    private readonly record struct ResolvedStockLine(long MaterialId, string Code, string Name, string? Unit);

    /// <summary>
    /// 一次库存移动：改台账 + 记流水。delta 带符号；负到库存不足时整单事务回滚。
    /// line 参数只取快照字段，作废路径上传的是从流水还原出来的行。
    /// </summary>
    private async Task MoveAsync(long warehouseId, string warehouseName, ScmStockDocLine line, decimal delta,
        ScmStockDoc doc, DateTime bizTime, string operatorName)
        => await MoveCoreAsync(warehouseId, warehouseName, new ResolvedStockLine(line.MaterialId, line.MaterialCode,
            line.MaterialName, line.Unit), delta, doc, bizTime, operatorName);

    private async Task MoveAsync(long warehouseId, string warehouseName, ResolvedStockLine line, decimal delta,
        ScmStockDoc doc, DateTime bizTime, string operatorName)
        => await MoveCoreAsync(warehouseId, warehouseName, line, delta, doc, bizTime, operatorName);

    private async Task MoveCoreAsync(long warehouseId, string warehouseName, ResolvedStockLine line, decimal delta,
        ScmStockDoc doc, DateTime bizTime, string operatorName)
    {
        if (delta == 0) return; // 盘点无差异：不记流水，免得账本里全是 0

        var stock = await stockRepo.FindAsync(s => s.WarehouseId == warehouseId && s.MaterialId == line.MaterialId);
        var before = stock?.Quantity ?? 0m;
        var after = before + delta;
        if (after < 0)
            throw new BizException($"「{line.Name}」在「{warehouseName}」库存不足：当前 {before:0.####}，本次要出 {Math.Abs(delta):0.####}");

        if (stock is null)
        {
            await stockRepo.InsertAsync(new ScmStock
            {
                WarehouseId = warehouseId, WarehouseName = warehouseName, MaterialId = line.MaterialId,
                MaterialCode = line.Code, MaterialName = line.Name, Unit = line.Unit, Quantity = after,
                UpdateTime = bizTime
            });
        }
        else
        {
            stock.Quantity = after;
            stock.MaterialCode = line.Code;
            stock.MaterialName = line.Name;
            stock.Unit = line.Unit;
            stock.WarehouseName = warehouseName;
            // UpdateTime 由 AOP 赋值，但 UpdateColumns 只写列名清单里的列——不带上「最后变动」就永远是空
            await stockRepo.UpdateColumnsAsync(stock, "Quantity", "MaterialCode", "MaterialName", "Unit",
                "WarehouseName", "UpdateTime");

        }

        await ledgerRepo.InsertAsync(new ScmStockLedger
        {
            DocId = doc.Id, DocNo = doc.DocNo, Kind = doc.Kind, WarehouseId = warehouseId,
            WarehouseName = warehouseName, MaterialId = line.MaterialId, MaterialCode = line.Code,
            MaterialName = line.Name, Unit = line.Unit, ChangeQty = delta, BeforeQty = before, AfterQty = after,
            BizTime = bizTime, OperatorName = operatorName
        });
    }

    private async Task<decimal> BookQtyAsync(long warehouseId, long materialId)
        => (await stockRepo.FindAsync(s => s.WarehouseId == warehouseId && s.MaterialId == materialId))?.Quantity ?? 0m;

    /* ---------------------------------- 校验 ---------------------------------- */

    private async Task<(MdWarehouse Warehouse, MdWarehouse? Target)> ResolveWarehousesAsync(StockDocSaveDto dto)
    {
        if (!long.TryParse(dto.WarehouseId, out var warehouseId) || warehouseId <= 0)
            throw new BizException("请选择仓库");
        var warehouse = await warehouses.FindAsync(warehouseId) ?? throw new BizException("仓库不存在");

        if (dto.Kind != StockDocKind.Transfer && !string.IsNullOrWhiteSpace(dto.TargetWarehouseId))
            throw new BizException("只有调拨单需要目标仓库");
        if (dto.Kind != StockDocKind.Transfer) return (warehouse, null);

        if (!long.TryParse(dto.TargetWarehouseId, out var targetId) || targetId <= 0)
            throw new BizException("调拨单请选择目标仓库");
        if (targetId == warehouseId) throw new BizException("调拨的目标仓库不能与源仓库相同");
        var target = await warehouses.FindAsync(targetId) ?? throw new BizException("目标仓库不存在");
        return (warehouse, target);
    }

    private async Task<List<ScmStockDocLine>> ResolveLinesAsync(StockDocSaveDto dto)
    {
        if (dto.Lines.Count == 0) throw new BizException("请至少录入一行明细");
        var ids = dto.Lines.Select(l => ParseMaterialId(l.MaterialId)).Distinct().ToList();
        var known = (await materials.ListAsync(m => ids.Contains(m.Id))).ToDictionary(m => m.Id);

        var lines = new List<ScmStockDocLine>(dto.Lines.Count);
        for (var i = 0; i < dto.Lines.Count; i++)
        {
            var raw = dto.Lines[i];
            var materialId = ParseMaterialId(raw.MaterialId);
            if (!known.TryGetValue(materialId, out var material))
                throw new BizException($"第 {i + 1} 行的物料不存在或已被删除");
            if (lines.Any(l => l.MaterialId == materialId))
                throw new BizException($"第 {i + 1} 行物料重复，同一张单请合并数量");
            // 盘点允许实盘 0（盘亏到零），其余类型数量为 0 没有意义
            if (raw.Quantity < 0 || (dto.Kind != StockDocKind.Count && raw.Quantity <= 0))
                throw new BizException($"第 {i + 1} 行数量不合法");

            lines.Add(new ScmStockDocLine
            {
                MaterialId = materialId, MaterialCode = material.MaterialCode, MaterialName = material.MaterialName,
                Spec = material.Spec, Unit = material.Unit, Quantity = raw.Quantity, Remark = Blank.ToNull(raw.Remark)
            });
        }

        return lines;
    }

    private void GuardDraft(ScmStockDoc doc, long userId)
    {
        if (doc.OwnerUserId != userId) throw BizException.Forbidden("只能操作本人单据");
        if (doc.Status != StockDocStatus.Draft) throw new BizException("已过账/已作废的单据不可修改");
    }

    private async Task InsertLinesAsync(long docId, StockDocSaveDto dto, List<ScmStockDocLine> lines)
    {
        lines.ForEach(l => l.DocId = docId);
        await lineRepo.InsertRangeAsync(lines);
    }

    private async Task<string> NextDocNoAsync(StockDocKind kind)
    {
        var prefix = Prefixes[kind] + DateTime.Now.ToString("yyyyMMdd");
        var count = await Repo.CountAsync(d => d.DocNo.StartsWith(prefix));
        return $"{prefix}{count + 1:D3}";
    }

    private static long ParseMaterialId(string raw)
        => long.TryParse(raw, out var id) && id > 0 ? id : throw new BizException("请选择物料");

    private async Task FillLineCountsAsync(List<StockDocDto> rows)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => long.Parse(r.Id)).ToList();
        var counts = (await lineRepo.ListAsync(l => ids.Contains(l.DocId)))
            .GroupBy(l => l.DocId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var r in rows) r.LineCount = counts.GetValueOrDefault(long.Parse(r.Id));
    }

    private static StockDocLineDto ToLineDto(ScmStockDocLine l) => new()
    {
        Id = l.Id.ToString(), MaterialId = l.MaterialId.ToString(), MaterialCode = l.MaterialCode,
        MaterialName = l.MaterialName, Spec = l.Spec, Unit = l.Unit, Quantity = l.Quantity, Remark = l.Remark
    };

    private static StockDocDto ToDto(ScmStockDoc d) => new()
    {
        Id = d.Id.ToString(), DocNo = d.DocNo, Kind = d.Kind, WarehouseId = d.WarehouseId.ToString(),
        WarehouseName = d.WarehouseName, TargetWarehouseId = d.TargetWarehouseId?.ToString(),
        TargetWarehouseName = d.TargetWarehouseName, BizDate = d.BizDate, SourceOrderNo = d.SourceOrderNo,
        TotalQty = d.TotalQty, Status = d.Status, PostedTime = d.PostedTime,
        OwnerUserId = d.OwnerUserId?.ToString() ?? "", OwnerUserName = d.OwnerUserName, Remark = d.Remark,
        CreateTime = d.CreateTime, Version = d.Version
    };
}

/// <summary>库存台账（仓库 × 物料现存量）。全局事实，不按人切——切了就看不出可用量。</summary>
public class StockService(IRepository<ScmStock> repo) : BaseService<ScmStock>(repo)
{
    public async Task<PagedResult<StockDto>> PageAsync(StockQuery query)
    {
        var kw = query.Keyword?.Trim();
        var exp = Expressionable.Create<ScmStock>();
        if (!string.IsNullOrEmpty(kw)) exp.And(s => s.MaterialName.Contains(kw) || s.MaterialCode.Contains(kw)
            || s.WarehouseName.Contains(kw));
        if (long.TryParse(query.WarehouseId, out var warehouseId)) exp.And(s => s.WarehouseId == warehouseId);
        if (query.OnlyPositive) exp.And(s => s.Quantity > 0);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["materialCode"] = "material_code" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<StockDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    private static StockDto ToDto(ScmStock s) => new()
    {
        Id = s.Id.ToString(), WarehouseId = s.WarehouseId.ToString(), WarehouseName = s.WarehouseName,
        MaterialId = s.MaterialId.ToString(), MaterialCode = s.MaterialCode, MaterialName = s.MaterialName,
        Spec = s.Spec, Unit = s.Unit, Quantity = s.Quantity, UpdateTime = s.UpdateTime
    };
}

/// <summary>库存流水（只读）。变动前/后结存一并存下来，对账时不必重放历史。</summary>
public class LedgerService(IRepository<ScmStockLedger> repo) : BaseService<ScmStockLedger>(repo)
{
    public async Task<PagedResult<LedgerDto>> PageAsync(LedgerQuery query)
    {
        var kw = query.Keyword?.Trim();
        var exp = Expressionable.Create<ScmStockLedger>();
        if (!string.IsNullOrEmpty(kw)) exp.And(l => l.DocNo.Contains(kw) || l.MaterialName.Contains(kw)
            || l.MaterialCode.Contains(kw) || l.WarehouseName.Contains(kw));
        if (query.Kind is StockDocKind kind) exp.And(l => l.Kind == kind);
        if (long.TryParse(query.WarehouseId, out var warehouseId)) exp.And(l => l.WarehouseId == warehouseId);
        if (long.TryParse(query.MaterialId, out var materialId)) exp.And(l => l.MaterialId == materialId);
        if (query.Begin is DateTime begin) exp.And(l => l.BizTime >= begin);
        if (query.End is DateTime end) exp.And(l => l.BizTime <= end);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["bizTime"] = "biz_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<LedgerDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    private static LedgerDto ToDto(ScmStockLedger l) => new()
    {
        Id = l.Id.ToString(), DocId = l.DocId.ToString(), DocNo = l.DocNo, Kind = l.Kind,
        WarehouseId = l.WarehouseId.ToString(), WarehouseName = l.WarehouseName, MaterialId = l.MaterialId.ToString(),
        MaterialCode = l.MaterialCode, MaterialName = l.MaterialName, Unit = l.Unit, ChangeQty = l.ChangeQty,
        BeforeQty = l.BeforeQty, AfterQty = l.AfterQty, BizTime = l.BizTime, OperatorName = l.OperatorName
    };
}
