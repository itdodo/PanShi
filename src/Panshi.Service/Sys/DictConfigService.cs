using Panshi.Common.Cache;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Repository;
using Panshi.Service.Base;
using SqlSugar;

namespace Panshi.Service.Sys;

/// <summary>字典服务（类型+数据项；按编码取数走缓存，保存即失效）。</summary>
public class DictService(
    IRepository<SysDictType> typeRepo,
    IRepository<SysDictData> dataRepo,
    Panshi.Common.Cache.ICacheService cache)
{
    private const string DataCachePrefix = "sys:dict:data:";

    // ----- 类型 -----
    public async Task<PagedResult<DictTypeDto>> TypePageAsync(DictTypeQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var exp = Expressionable.Create<SysDictType>();
        if (!string.IsNullOrEmpty(keyword)) exp.And(t => t.DictName.Contains(keyword) || t.DictCode.Contains(keyword));
        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await typeRepo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<DictTypeDto>
        {
            Total = page.Total,
            Rows = page.Rows.Select(t => new DictTypeDto
            {
                Id = t.Id.ToString(), DictName = t.DictName, DictCode = t.DictCode, Remark = t.Remark,
                CreateTime = t.CreateTime, Version = t.Version
            }).ToList()
        };
    }

    public async Task<List<DictTypeDto>> TypeListAsync()
        => (await typeRepo.ListAsync()).Select(t => new DictTypeDto
        {
            Id = t.Id.ToString(), DictName = t.DictName, DictCode = t.DictCode, Remark = t.Remark,
            CreateTime = t.CreateTime, Version = t.Version
        }).ToList();

    public async Task<DictTypeDto> CreateTypeAsync(DictTypeSaveDto dto)
    {
        if (await typeRepo.ExistsAsync(t => t.DictCode == dto.DictCode)) throw new BizException("字典编码已存在");
        var t = new SysDictType { DictCode = dto.DictCode.Trim(), DictName = dto.DictName, Remark = dto.Remark };
        await typeRepo.InsertAsync(t);
        return ToTypeDto(t);
    }

    public async Task UpdateTypeAsync(long id, DictTypeSaveDto dto)
    {
        var t = await typeRepo.GetAsync(id);
        if (t.DictCode != dto.DictCode && await typeRepo.ExistsAsync(x => x.DictCode == dto.DictCode))
            throw new BizException("字典编码已存在");
        t.DictName = dto.DictName;
        t.DictCode = dto.DictCode.Trim();
        t.Remark = dto.Remark;
        t.Version = dto.Version;
        await typeRepo.UpdateWithAuditAsync(t, t.Version);
        Invalidate(t.DictCode);
    }

    public async Task DeleteTypeAsync(long id)
    {
        var t = await typeRepo.GetAsync(id);
        if (await dataRepo.ExistsAsync(d => d.DictTypeId == id)) throw new BizException("请先删除字典项");
        await typeRepo.SoftDeleteAsync(id);
        Invalidate(t.DictCode);
    }

    // ----- 数据项 -----
    public async Task<List<DictDataDto>> DataListAsync(long typeId)
        => (await dataRepo.ListAsync(d => d.DictTypeId == typeId)).OrderBy(d => d.Sort)
            .Select(ToDataDto).ToList();

    /// <summary>按字典编码取正常项（前端下拉；缓存）。</summary>
    public async Task<List<DictDataDto>> ByCodeAsync(string code)
    {
        var type = await typeRepo.FindAsync(t => t.DictCode == code);
        if (type is null) return [];
        return await cache.GetAsync(DataCachePrefix + code,
            async () => (await dataRepo.ListAsync(d => d.DictTypeId == type.Id && d.Status == 0))
                .OrderBy(d => d.Sort).Select(ToDataDto).ToList()) ?? [];
    }

    public async Task<DictDataDto> CreateDataAsync(DictDataSaveDto dto)
    {
        var type = await typeRepo.GetAsync(dto.DictTypeId);
        if (await dataRepo.ExistsAsync(d => d.DictTypeId == dto.DictTypeId && d.Value == dto.Value))
            throw new BizException("同字典下键值已存在");
        var data = new SysDictData
        {
            DictTypeId = dto.DictTypeId, Label = dto.Label, Value = dto.Value, Sort = dto.Sort,
            Status = dto.Status, TagType = dto.TagType, IsDefault = dto.IsDefault
        };
        await dataRepo.InsertAsync(data);
        Invalidate(type.DictCode);
        return ToDataDto(data);
    }

    public async Task UpdateDataAsync(long id, DictDataSaveDto dto)
    {
        var data = await dataRepo.GetAsync(id);
        var type = await typeRepo.GetAsync(data.DictTypeId);
        data.Label = dto.Label;
        data.Value = dto.Value;
        data.Sort = dto.Sort;
        data.Status = dto.Status;
        data.TagType = dto.TagType;
        data.IsDefault = dto.IsDefault;
        data.Version = dto.Version;
        await dataRepo.UpdateWithAuditAsync(data, data.Version);
        Invalidate(type.DictCode);
    }

    public async Task DeleteDataAsync(long id)
    {
        var data = await dataRepo.GetAsync(id);
        var type = await typeRepo.GetAsync(data.DictTypeId);
        await dataRepo.SoftDeleteAsync(id);
        Invalidate(type.DictCode);
    }

    private void Invalidate(string code) => cache.Remove(DataCachePrefix + code);

    private static DictTypeDto ToTypeDto(SysDictType t) => new()
    {
        Id = t.Id.ToString(), DictName = t.DictName, DictCode = t.DictCode, Remark = t.Remark,
        CreateTime = t.CreateTime, Version = t.Version
    };

    private static DictDataDto ToDataDto(SysDictData d) => new()
    {
        Id = d.Id.ToString(), DictTypeId = d.DictTypeId.ToString(), Label = d.Label, Value = d.Value,
        Sort = d.Sort, Status = d.Status, TagType = d.TagType, IsDefault = d.IsDefault, Version = d.Version
    };
}

/// <summary>参数管理（内置键禁删、键不可改）。</summary>
public class ConfigAdminService(
    IRepository<SysConfig> repo,
    ConfigService config) : BaseService<SysConfig>(repo)
{
    public async Task<PagedResult<ConfigDto>> PageAsync(Model.Dtos.ConfigQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var exp = Expressionable.Create<SysConfig>();
        if (!string.IsNullOrEmpty(keyword)) exp.And(c => c.ConfigName.Contains(keyword) || c.ConfigKey.Contains(keyword));
        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<ConfigDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    public async Task<ConfigDto> CreateAsync(ConfigSaveDto dto)
    {
        if (await Repo.ExistsAsync(c => c.ConfigKey == dto.ConfigKey)) throw new BizException("参数键已存在");
        var cfg = new SysConfig
        {
            ConfigKey = dto.ConfigKey.Trim(), ConfigName = dto.ConfigName, ConfigValue = dto.ConfigValue,
            Remark = dto.Remark
        };
        await Repo.InsertAsync(cfg);
        Invalidate();
        return ToDto(cfg);
    }

    public async Task UpdateAsync(long id, ConfigSaveDto dto)
    {
        var cfg = await Repo.GetAsync(id);
        if (cfg.ConfigKey != dto.ConfigKey.Trim()) throw new BizException("内置/已引用参数键不可修改");
        cfg.ConfigName = dto.ConfigName;
        cfg.ConfigValue = dto.ConfigValue;
        cfg.Remark = dto.Remark;
        cfg.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(cfg);
        Invalidate();
    }

    public async Task DeleteAsync(long id)
    {
        var cfg = await Repo.GetAsync(id);
        if (cfg.BuiltIn) throw new BizException("内置参数不可删除");
        await Repo.SoftDeleteAsync(id);
        Invalidate();
    }

    private void Invalidate() => config.Invalidate();

    private static ConfigDto ToDto(SysConfig c) => new()
    {
        Id = c.Id.ToString(), ConfigName = c.ConfigName, ConfigKey = c.ConfigKey,
        ConfigValue = c.ConfigValue, BuiltIn = c.BuiltIn, Remark = c.Remark, Version = c.Version
    };
}
