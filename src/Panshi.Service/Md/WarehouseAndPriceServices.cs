using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using SqlSugar;

namespace Panshi.Service.Md;

/// <summary>仓库主数据。默认仓全库至多一个，由本服务在写入时互斥维护。</summary>
public class WarehouseService(IRepository<MdWarehouse> repo) : BaseService<MdWarehouse>(repo)
{
    public async Task<PagedResult<WarehouseDto>> PageAsync(WarehouseQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var exp = Expressionable.Create<MdWarehouse>();
        if (!string.IsNullOrEmpty(keyword))
            exp.And(w => w.WarehouseName.Contains(keyword) || w.WarehouseCode.Contains(keyword));
        if (query.Status is EnableStatus st) exp.And(w => w.Status == st);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string>
        {
            ["createTime"] = "create_time", ["warehouseCode"] = "warehouse_code"
        });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<WarehouseDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    public async Task<List<OptionDto>> OptionsAsync()
        => (await Repo.ListAsync(w => w.Status == EnableStatus.Enabled))
            .OrderBy(w => w.WarehouseCode, StringComparer.Ordinal)
            .Select(w => new OptionDto { Value = w.Id.ToString(), Label = $"{w.WarehouseCode} {w.WarehouseName}" })
            .ToList();

    public async Task<WarehouseDto> CreateAsync(WarehouseSaveDto dto)
    {
        var code = dto.WarehouseCode.Trim();
        if (await Repo.ExistsAsync(w => w.WarehouseCode == code)) throw new BizException("仓库编码已存在");
        var w = new MdWarehouse { WarehouseCode = code };
        Apply(w, dto);
        await Repo.InsertAsync(w);
        if (w.IsDefault) await ClearOtherDefaultsAsync(w.Id);
        return ToDto(w);
    }

    public async Task UpdateAsync(long id, WarehouseSaveDto dto)
    {
        var w = await Repo.GetAsync(id);
        var code = dto.WarehouseCode.Trim();
        if (w.WarehouseCode != code && await Repo.ExistsAsync(x => x.WarehouseCode == code))
            throw new BizException("仓库编码已存在");
        w.WarehouseCode = code;
        Apply(w, dto);
        w.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(w);
        if (w.IsDefault) await ClearOtherDefaultsAsync(w.Id);
    }

    public Task DeleteAsync(long id) => Repo.SoftDeleteAsync(id);

    private async Task ClearOtherDefaultsAsync(long keepId)
    {
        foreach (var other in await Repo.ListAsync(x => x.IsDefault && x.Id != keepId))
        {
            other.IsDefault = false;
            await Repo.UpdateColumnsAsync(other, "IsDefault");
        }
    }

    private static void Apply(MdWarehouse w, WarehouseSaveDto dto)
    {
        w.WarehouseName = dto.WarehouseName.Trim();
        w.Address = Blank.ToNull(dto.Address);
        w.Contact = Blank.ToNull(dto.Contact);
        w.Phone = Blank.ToNull(dto.Phone);
        w.IsDefault = dto.IsDefault;
        w.Status = dto.Status;
        w.Remark = Blank.ToNull(dto.Remark);
    }

    private static WarehouseDto ToDto(MdWarehouse w) => new()
    {
        Id = w.Id.ToString(), WarehouseCode = w.WarehouseCode, WarehouseName = w.WarehouseName, Address = w.Address,
        Contact = w.Contact, Phone = w.Phone, IsDefault = w.IsDefault, Status = w.Status, Remark = w.Remark,
        CreateTime = w.CreateTime, Version = w.Version
    };
}

/// <summary>
/// 采购协议价。供应商/物料名称按 id 取快照存下来——主数据改名不该让历史报价看不出是谁的价。
/// 一个供应商 × 一个物料只留一行现行价（迁移 0005 建软删过滤唯一索引），调价直接改这行，
/// 历史由字段级变更日志回溯。
/// </summary>
public class PriceAgreementService(
    IRepository<MdPriceAgreement> repo,
    IRepository<MdSupplier> suppliers,
    IRepository<MdMaterial> materials) : BaseService<MdPriceAgreement>(repo)
{
    public async Task<PagedResult<PriceAgreementDto>> PageAsync(PriceAgreementQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var exp = Expressionable.Create<MdPriceAgreement>();
        if (long.TryParse(query.SupplierId, out var supplierId)) exp.And(p => p.SupplierId == supplierId);
        if (!string.IsNullOrEmpty(keyword))
            exp.And(p => p.MaterialName.Contains(keyword) || p.MaterialCode.Contains(keyword)
                || p.SupplierName.Contains(keyword));
        if (query.Status is EnableStatus st) exp.And(p => p.Status == st);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<PriceAgreementDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    /// <summary>下单选料时带出价格：只认「启用 + 今天在生效区间内」的协议价，多条重叠取生效日最新的一条。</summary>
    public async Task<PriceAgreementDto?> QuoteAsync(long supplierId, long materialId)
    {
        var today = DateTime.Today;
        var rows = await Repo.ListAsync(p => p.SupplierId == supplierId && p.MaterialId == materialId
            && p.Status == EnableStatus.Enabled);
        var hit = rows.Where(p => (p.BeginDate is null || p.BeginDate <= today) && (p.EndDate is null || p.EndDate >= today))
            .OrderByDescending(p => p.BeginDate ?? DateTime.MinValue)
            .FirstOrDefault();
        return hit is null ? null : ToDto(hit);
    }

    public async Task<PriceAgreementDto> CreateAsync(PriceAgreementSaveDto dto)
    {
        var (supplierId, materialId) = ParseIds(dto);
        GuardRange(dto);
        if (await Repo.ExistsAsync(p => p.SupplierId == supplierId && p.MaterialId == materialId))
            throw new BizException("该供应商对这个物料已有协议价，请直接编辑那一条");

        var supplier = await suppliers.FindAsync(supplierId) ?? throw new BizException("供应商不存在");
        var material = await materials.FindAsync(materialId) ?? throw new BizException("物料不存在");
        var p = new MdPriceAgreement
        {
            SupplierId = supplierId, MaterialId = materialId, SupplierName = supplier.SupplierName,
            MaterialCode = material.MaterialCode, MaterialName = material.MaterialName
        };
        Apply(p, dto);
        await Repo.InsertAsync(p);
        return ToDto(p);
    }

    public async Task UpdateAsync(long id, PriceAgreementSaveDto dto)
    {
        var p = await Repo.GetAsync(id);
        var (supplierId, materialId) = ParseIds(dto);
        GuardRange(dto);
        if ((p.SupplierId != supplierId || p.MaterialId != materialId)
            && await Repo.ExistsAsync(x => x.SupplierId == supplierId && x.MaterialId == materialId))
            throw new BizException("该供应商对这个物料已有协议价");

        // 换供应商/换物料时重新取快照，否则名称会停在旧对象上
        if (p.SupplierId != supplierId)
        {
            var supplier = await suppliers.FindAsync(supplierId) ?? throw new BizException("供应商不存在");
            p.SupplierName = supplier.SupplierName;
        }

        if (p.MaterialId != materialId)
        {
            var material = await materials.FindAsync(materialId) ?? throw new BizException("物料不存在");
            p.MaterialCode = material.MaterialCode;
            p.MaterialName = material.MaterialName;
        }

        p.SupplierId = supplierId;
        p.MaterialId = materialId;
        Apply(p, dto);
        p.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(p);
    }

    public Task DeleteAsync(long id) => Repo.SoftDeleteAsync(id);

    private static (long SupplierId, long MaterialId) ParseIds(PriceAgreementSaveDto dto)
    {
        if (!long.TryParse(dto.SupplierId, out var supplierId) || supplierId <= 0) throw new BizException("请选择供应商");
        if (!long.TryParse(dto.MaterialId, out var materialId) || materialId <= 0) throw new BizException("请选择物料");
        return (supplierId, materialId);
    }

    private static void GuardRange(PriceAgreementSaveDto dto)
    {
        if (dto is { BeginDate: { } begin, EndDate: { } end } && end.Date < begin.Date)
            throw new BizException("失效日不能早于生效日");
    }

    private static void Apply(MdPriceAgreement p, PriceAgreementSaveDto dto)
    {
        p.UnitPrice = dto.UnitPrice;
        p.TaxRate = dto.TaxRate;
        p.BeginDate = dto.BeginDate?.Date;
        p.EndDate = dto.EndDate?.Date;
        p.Status = dto.Status;
        p.Remark = Blank.ToNull(dto.Remark);
    }

    private static PriceAgreementDto ToDto(MdPriceAgreement p) => new()
    {
        Id = p.Id.ToString(), SupplierId = p.SupplierId.ToString(), SupplierName = p.SupplierName,
        MaterialId = p.MaterialId.ToString(), MaterialCode = p.MaterialCode, MaterialName = p.MaterialName,
        UnitPrice = p.UnitPrice, TaxRate = p.TaxRate, BeginDate = p.BeginDate, EndDate = p.EndDate,
        Status = p.Status, Remark = p.Remark, CreateTime = p.CreateTime, Version = p.Version
    };
}
