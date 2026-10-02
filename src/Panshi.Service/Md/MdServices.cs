using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using SqlSugar;

namespace Panshi.Service.Md;

/// <summary>
/// 基础资料三张表的服务。刻意不抽公共泛型基类：三者的查询列、唯一性字段名与 DTO 映射各不相同，
/// 抽出来只会变成一堆 protected abstract 钩子，读一个页面要跳三个文件。
/// 共同点只有「编码唯一 + 软删 + 乐观锁」，与岗位/字典一致，按仓库既有样板各写一份。
/// </summary>
public class MaterialService(IRepository<MdMaterial> repo) : BaseService<MdMaterial>(repo)
{
    public async Task<PagedResult<MaterialDto>> PageAsync(MaterialQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var category = query.Category?.Trim();
        var exp = Expressionable.Create<MdMaterial>();
        if (!string.IsNullOrEmpty(keyword))
            exp.And(m => m.MaterialName.Contains(keyword) || m.MaterialCode.Contains(keyword)
                || (m.Spec != null && m.Spec.Contains(keyword)));
        if (!string.IsNullOrEmpty(category)) exp.And(m => m.Category == category);
        if (query.Status is EnableStatus st) exp.And(m => m.Status == st);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string>
        {
            ["createTime"] = "create_time", ["materialCode"] = "material_code"
        });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<MaterialDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    /// <summary>单据下拉用：只回启用的，按编码序。</summary>
    public async Task<List<OptionDto>> OptionsAsync()
        => (await Repo.ListAsync(m => m.Status == EnableStatus.Enabled)).OrderBy(m => m.MaterialCode, StringComparer.Ordinal)
            .Select(m => new OptionDto
            {
                Value = m.Id.ToString(),
                Label = string.IsNullOrEmpty(m.Spec) ? $"{m.MaterialCode} {m.MaterialName}" : $"{m.MaterialCode} {m.MaterialName} / {m.Spec}"
            }).ToList();

    public async Task<MaterialDto> CreateAsync(MaterialSaveDto dto)
    {
        var code = dto.MaterialCode.Trim();
        if (await Repo.ExistsAsync(m => m.MaterialCode == code)) throw new BizException("物料编码已存在");
        var m = new MdMaterial { MaterialCode = code };
        Apply(m, dto);
        await Repo.InsertAsync(m);
        return ToDto(m);
    }

    public async Task UpdateAsync(long id, MaterialSaveDto dto)
    {
        var m = await Repo.GetAsync(id);
        var code = dto.MaterialCode.Trim();
        if (m.MaterialCode != code && await Repo.ExistsAsync(x => x.MaterialCode == code))
            throw new BizException("物料编码已存在");
        m.MaterialCode = code;
        Apply(m, dto);
        m.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(m);
    }

    public Task DeleteAsync(long id) => Repo.SoftDeleteAsync(id);

    private static void Apply(MdMaterial m, MaterialSaveDto dto)
    {
        m.MaterialName = dto.MaterialName.Trim();
        m.Category = Blank.ToNull(dto.Category);
        m.Spec = Blank.ToNull(dto.Spec);
        m.Unit = Blank.ToNull(dto.Unit);
        m.PurchasePrice = dto.PurchasePrice;
        m.SalePrice = dto.SalePrice;
        if (dto.MinStock is < 0 || dto.MaxStock is < 0) throw new BizException("预警上下限不能为负");
        if (dto.MinStock is { } min && dto.MaxStock is { } max && max <= min)
            throw new BizException("预警上限要大于下限，否则任何库存量都会同时报两种预警");
        m.MinStock = dto.MinStock;
        m.MaxStock = dto.MaxStock;
        m.Status = dto.Status;
        m.Remark = Blank.ToNull(dto.Remark);
    }

    private static MaterialDto ToDto(MdMaterial m) => new()
    {
        Id = m.Id.ToString(), MaterialCode = m.MaterialCode, MaterialName = m.MaterialName, Category = m.Category,
        Spec = m.Spec, Unit = m.Unit, PurchasePrice = m.PurchasePrice, SalePrice = m.SalePrice,
        MinStock = m.MinStock, MaxStock = m.MaxStock, Status = m.Status,
        Remark = m.Remark, CreateTime = m.CreateTime, Version = m.Version
    };
}

/// <summary>供应商主数据。</summary>
public class SupplierService(IRepository<MdSupplier> repo) : BaseService<MdSupplier>(repo)
{
    public async Task<PagedResult<SupplierDto>> PageAsync(SupplierQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var category = query.Category?.Trim();
        var exp = Expressionable.Create<MdSupplier>();
        if (!string.IsNullOrEmpty(keyword))
            exp.And(s => s.SupplierName.Contains(keyword) || s.SupplierCode.Contains(keyword)
                || (s.ShortName != null && s.ShortName.Contains(keyword))
                || (s.Contact != null && s.Contact.Contains(keyword)));
        if (!string.IsNullOrEmpty(category)) exp.And(s => s.Category == category);
        if (query.Status is EnableStatus st) exp.And(s => s.Status == st);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string>
        {
            ["createTime"] = "create_time", ["supplierCode"] = "supplier_code"
        });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<SupplierDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    public async Task<List<OptionDto>> OptionsAsync()
        => (await Repo.ListAsync(s => s.Status == EnableStatus.Enabled)).OrderBy(s => s.SupplierCode, StringComparer.Ordinal)
            .Select(s => new OptionDto { Value = s.Id.ToString(), Label = $"{s.SupplierCode} {s.SupplierName}" })
            .ToList();

    public async Task<SupplierDto> CreateAsync(SupplierSaveDto dto)
    {
        var code = dto.SupplierCode.Trim();
        if (await Repo.ExistsAsync(s => s.SupplierCode == code)) throw new BizException("供应商编码已存在");
        var s = new MdSupplier { SupplierCode = code };
        Apply(s, dto);
        await Repo.InsertAsync(s);
        return ToDto(s);
    }

    public async Task UpdateAsync(long id, SupplierSaveDto dto)
    {
        var s = await Repo.GetAsync(id);
        var code = dto.SupplierCode.Trim();
        if (s.SupplierCode != code && await Repo.ExistsAsync(x => x.SupplierCode == code))
            throw new BizException("供应商编码已存在");
        s.SupplierCode = code;
        Apply(s, dto);
        s.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(s);
    }

    public Task DeleteAsync(long id) => Repo.SoftDeleteAsync(id);

    private static void Apply(MdSupplier s, SupplierSaveDto dto)
    {
        s.SupplierName = dto.SupplierName.Trim();
        s.ShortName = Blank.ToNull(dto.ShortName);
        s.TaxNo = Blank.ToNull(dto.TaxNo, t => t.ToUpperInvariant());
        s.Contact = Blank.ToNull(dto.Contact);
        s.Phone = Blank.ToNull(dto.Phone);
        s.Email = Blank.ToNull(dto.Email);
        s.Address = Blank.ToNull(dto.Address);
        s.BankName = Blank.ToNull(dto.BankName);
        s.BankAccount = Blank.ToNull(dto.BankAccount);
        s.Category = Blank.ToNull(dto.Category);
        s.Status = dto.Status;
        s.Remark = Blank.ToNull(dto.Remark);
    }

    private static SupplierDto ToDto(MdSupplier s) => new()
    {
        Id = s.Id.ToString(), SupplierCode = s.SupplierCode, SupplierName = s.SupplierName, ShortName = s.ShortName,
        TaxNo = s.TaxNo, Contact = s.Contact, Phone = s.Phone, Email = s.Email, Address = s.Address,
        BankName = s.BankName, BankAccount = s.BankAccount, Category = s.Category, Status = s.Status,
        Remark = s.Remark, CreateTime = s.CreateTime, Version = s.Version
    };
}

/// <summary>客户主数据。</summary>
public class CustomerService(IRepository<MdCustomer> repo) : BaseService<MdCustomer>(repo)
{
    public async Task<PagedResult<CustomerDto>> PageAsync(CustomerQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var level = query.Level?.Trim();
        var exp = Expressionable.Create<MdCustomer>();
        if (!string.IsNullOrEmpty(keyword))
            exp.And(c => c.CustomerName.Contains(keyword) || c.CustomerCode.Contains(keyword)
                || (c.ShortName != null && c.ShortName.Contains(keyword))
                || (c.Contact != null && c.Contact.Contains(keyword)));
        if (!string.IsNullOrEmpty(level)) exp.And(c => c.Level == level);
        if (query.Status is EnableStatus st) exp.And(c => c.Status == st);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string>
        {
            ["createTime"] = "create_time", ["customerCode"] = "customer_code"
        });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<CustomerDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    public async Task<List<OptionDto>> OptionsAsync()
        => (await Repo.ListAsync(c => c.Status == EnableStatus.Enabled)).OrderBy(c => c.CustomerCode, StringComparer.Ordinal)
            .Select(c => new OptionDto { Value = c.Id.ToString(), Label = $"{c.CustomerCode} {c.CustomerName}" })
            .ToList();

    public async Task<CustomerDto> CreateAsync(CustomerSaveDto dto)
    {
        var code = dto.CustomerCode.Trim();
        if (await Repo.ExistsAsync(c => c.CustomerCode == code)) throw new BizException("客户编码已存在");
        var c = new MdCustomer { CustomerCode = code };
        Apply(c, dto);
        await Repo.InsertAsync(c);
        return ToDto(c);
    }

    public async Task UpdateAsync(long id, CustomerSaveDto dto)
    {
        var c = await Repo.GetAsync(id);
        var code = dto.CustomerCode.Trim();
        if (c.CustomerCode != code && await Repo.ExistsAsync(x => x.CustomerCode == code))
            throw new BizException("客户编码已存在");
        c.CustomerCode = code;
        Apply(c, dto);
        c.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(c);
    }

    public Task DeleteAsync(long id) => Repo.SoftDeleteAsync(id);

    private static void Apply(MdCustomer c, CustomerSaveDto dto)
    {
        c.CustomerName = dto.CustomerName.Trim();
        c.ShortName = Blank.ToNull(dto.ShortName);
        c.TaxNo = Blank.ToNull(dto.TaxNo, t => t.ToUpperInvariant());
        c.Contact = Blank.ToNull(dto.Contact);
        c.Phone = Blank.ToNull(dto.Phone);
        c.Email = Blank.ToNull(dto.Email);
        c.Address = Blank.ToNull(dto.Address);
        c.Level = Blank.ToNull(dto.Level);
        c.Status = dto.Status;
        c.Remark = Blank.ToNull(dto.Remark);
    }

    private static CustomerDto ToDto(MdCustomer c) => new()
    {
        Id = c.Id.ToString(), CustomerCode = c.CustomerCode, CustomerName = c.CustomerName, ShortName = c.ShortName,
        TaxNo = c.TaxNo, Contact = c.Contact, Phone = c.Phone, Email = c.Email, Address = c.Address, Level = c.Level,
        Status = c.Status, Remark = c.Remark, CreateTime = c.CreateTime, Version = c.Version
    };
}

/// <summary>
/// 主数据的文本入库口径：全空白一律存 NULL。
/// 否则「填了空格再删干净」会留下一个非 NULL 的空串，列表上看着有值、查询条件又匹配不上。
/// </summary>
internal static class Blank
{
    internal static string? ToNull(string? value, Func<string, string>? also = null)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return also is null ? text : also(text);
    }
}
