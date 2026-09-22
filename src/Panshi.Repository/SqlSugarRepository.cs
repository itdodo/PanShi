using System.Linq.Expressions;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Common.Runtime;
using Panshi.Model.Entities;
using Panshi.Repository.Auditing;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>SqlSugar 泛型仓储实现。</summary>
public class SqlSugarRepository<T>(ISqlSugarClient db) : IRepository<T> where T : BaseEntity, new()
{
    private readonly EntityInfo _entity = db.EntityMaintenance.GetEntityInfo<T>();

    private static readonly string[] VersionLockExcluded =
    [
        nameof(BaseEntity.Id), nameof(BaseEntity.CreateTime), nameof(BaseEntity.CreateBy),
        nameof(BaseEntity.IsDeleted), nameof(BaseEntity.Version),
        nameof(BaseEntity.UpdateTime), nameof(BaseEntity.UpdateBy)
    ];

    public ISqlSugarClient Db { get; } = db;

    private string Table => _entity.DbTableName;

    /// <summary>乐观锁 UPDATE 的业务列（属性名→物理列，缓存）。</summary>
    private (string Prop, string Column)[]? _updatableCache;

    private (string Prop, string Column)[] Updatable => _updatableCache ??= _entity.Columns
        .Where(c => !c.IsIgnore && !c.IsPrimarykey && !VersionLockExcluded.Contains(c.PropertyName))
        .Select(c => (c.PropertyName, c.DbColumnName))
        .ToArray();

    public async Task<T?> FindAsync(long id) => await Db.Queryable<T>().FirstAsync(it => it.Id == id);

    public async Task<T?> FindAsync(Expression<Func<T, bool>> where) => await Db.Queryable<T>().FirstAsync(where);

    public async Task<T> GetAsync(long id)
        => await FindAsync(id) ?? throw BizException.NotFound($"{_entity.DbTableName} 记录");

    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? where = null)
        => where is null ? Db.Queryable<T>().ToListAsync() : Db.Queryable<T>().Where(where).ToListAsync();

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> where) => Db.Queryable<T>().Where(where).AnyAsync();

    public async Task<long> CountAsync(Expression<Func<T, bool>>? where = null)
        => where is null ? await Db.Queryable<T>().CountAsync() : await Db.Queryable<T>().Where(where).CountAsync();

    public async Task<PagedResult<T>> PageAsync(Expression<Func<T, bool>> where, int pageNum, int pageSize,
        string? sortColumn = null, bool desc = true)
    {
        RefAsync<int> total = 0;
        var order = $"{(string.IsNullOrEmpty(sortColumn) ? "create_time" : sortColumn)} {(desc ? "desc" : "asc")}, id desc";
        var rows = await Db.Queryable<T>().Where(where).OrderBy(order).ToPageListAsync(pageNum, pageSize, total);
        return new PagedResult<T> { Total = total, Rows = rows };
    }

    public async Task<T> InsertAsync(T entity)
    {
        await Db.Insertable(entity).ExecuteCommandAsync();
        return entity;
    }

    /// <summary>批量插入（红线 #8：直连 Insertable 不触发雪花 AOP，显式填充 Id/CreateTime/CreateBy）。</summary>
    public async Task<int> InsertRangeAsync(List<T> entities)
    {
        if (entities.Count == 0) return 0;
        var now = DateTime.Now;
        var by = OperationUser.UserId;
        foreach (var e in entities)
        {
            if (e.Id == 0) e.Id = SnowflakeId.NextId();
            if (e.CreateTime == default) e.CreateTime = now;
            e.CreateBy ??= by;
        }

        return await Db.Insertable(entities).ExecuteCommandAsync();
    }

    /// <summary>全列更新（不带乐观锁，仅框架内部维护列使用；业务编辑走 UpdateWithAuditAsync）。</summary>
    public async Task<bool> UpdateAsync(T entity)
        => await Db.Updateable(entity).ExecuteCommandAsync() > 0;

    public async Task<bool> UpdateColumnsAsync(T entity, params string[] camelFields)
    {
        var props = _entity.Columns
            .Where(c => camelFields.Any(f =>
                string.Equals(c.PropertyName, f, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.DbColumnName, f, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.PropertyName)
            .ToArray();
        if (props.Length == 0) return false;
        return await Db.Updateable(entity).UpdateColumns(props).ExecuteCommandAsync() > 0;
    }

    /// <summary>
    /// 乐观锁单条 SQL：SET 业务列+version+1 WHERE id AND version（红线 #2：禁 WhereColumns）。
    /// 0 行=版本冲突返回 false。
    /// </summary>
    public async Task<bool> UpdateWithVersionCheckAsync(T entity, int expectedVersion)
    {
        var now = DateTime.Now;
        var by = OperationUser.UserId;
        entity.UpdateTime = now;
        entity.UpdateBy = by;

        var cols = Updatable;
        var setSql = string.Join(", ", cols.Select(c => $"{c.Column}=@{c.Prop}"));
        var sql = $"update {Table} set {setSql}, update_time=@__ut, update_by=@__ub, version=version+1 " +
                  "where id=@__id and version=@__ver and is_deleted=false";

        var ps = cols.Select(c =>
        {
            var prop = GetProp(c.Prop);
            return MakeParam("@" + c.Prop, prop.GetValue(entity), prop.PropertyType);
        }).ToList();
        ps.Add(new SugarParameter("@__ut", now) { DbType = System.Data.DbType.DateTime });
        ps.Add(new SugarParameter("@__ub", (object?)by ?? DBNull.Value) { DbType = System.Data.DbType.Int64 });
        ps.Add(new SugarParameter("@__id", entity.Id) { DbType = System.Data.DbType.Int64 });
        ps.Add(new SugarParameter("@__ver", expectedVersion) { DbType = System.Data.DbType.Int32 });

        var rows = await Db.Ado.ExecuteCommandAsync(sql, ps.ToArray());
        if (rows > 0) entity.Version = expectedVersion + 1;
        return rows > 0;
    }

    /// <summary>统一编辑入口组件：乐观锁更新 + 字段级审计（冲突不落审计、无差异不落审计）。</summary>
    public async Task UpdateWithAuditAsync(T entity, int expectedVersion)
    {
        var old = await FindAsync(entity.Id) ?? throw BizException.NotFound($"{Table} 记录");
        var changes = AuditDiff.Diff(old, entity);
        await Tran.RunAsync(Db, async () =>
        {
            var ok = await UpdateWithVersionCheckAsync(entity, expectedVersion);
            if (!ok) throw BizException.Conflict();
            if (changes.Count > 0)
                await Db.Insertable(new SysChangeLog
                {
                    Id = SnowflakeId.NextId(),
                    TableName = Table,
                    RecordId = entity.Id,
                    Changes = AuditDiff.ToJson(changes),
                    UserId = OperationUser.UserId ?? 0,
                    UserName = OperationUser.UserName ?? ""
                }).ExecuteCommandAsync();
        });
    }

    public async Task<bool> SoftDeleteAsync(long id)
    {
        var by = OperationUser.UserId;
        var now = DateTime.Now;
        var rows = await Db.Updateable<T>()
            .SetColumns(it => it.IsDeleted == true)
            .SetColumns(it => it.UpdateTime == now)
            .SetColumns(it => it.UpdateBy == by)
            .Where(it => it.Id == id && !it.IsDeleted)
            .ExecuteCommandAsync();
        return rows > 0;
    }

    public async Task<int> SoftDeleteAsync(Expression<Func<T, bool>> where)
    {
        var by = OperationUser.UserId;
        var now = DateTime.Now;
        return await Db.Updateable<T>()
            .SetColumns(it => it.IsDeleted == true)
            .SetColumns(it => it.UpdateTime == now)
            .SetColumns(it => it.UpdateBy == by)
            .Where(where)
            .ExecuteCommandAsync();
    }

    public async Task<bool> DeleteAsync(long id) => await Db.Deleteable<T>().In(id).ExecuteCommandAsync() > 0;

    public async Task<int> DeleteAsync(Expression<Func<T, bool>> where)
        => await Db.Deleteable<T>().Where(where).ExecuteCommandAsync();

    private System.Reflection.PropertyInfo GetProp(string name)
        => typeof(T).GetProperty(name) ?? throw new InvalidOperationException($"{typeof(T).Name}.{name} 不存在");

    /// <summary>枚举→底层整型；null→DBNull+显式 DbType（PG 对无类型 NULL 参数会推断为 text 报 42804）。</summary>
    private static SugarParameter MakeParam(string name, object? value, Type clrType)
    {
        if (value is Enum e)
        {
            clrType = Enum.GetUnderlyingType(e.GetType());
            value = Convert.ChangeType(e, clrType);
        }

        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
        var dbType = underlying switch
        {
            _ when underlying == typeof(long) => System.Data.DbType.Int64,
            _ when underlying == typeof(int) => System.Data.DbType.Int32,
            _ when underlying == typeof(short) => System.Data.DbType.Int16,
            _ when underlying == typeof(decimal) => System.Data.DbType.Decimal,
            _ when underlying == typeof(double) => System.Data.DbType.Double,
            _ when underlying == typeof(float) => System.Data.DbType.Single,
            _ when underlying == typeof(bool) => System.Data.DbType.Boolean,
            _ when underlying == typeof(DateTime) => System.Data.DbType.DateTime,
            _ when underlying == typeof(Guid) => System.Data.DbType.Guid,
            _ => System.Data.DbType.String
        };
        return new SugarParameter(name, value ?? DBNull.Value) { DbType = dbType };
    }
}
