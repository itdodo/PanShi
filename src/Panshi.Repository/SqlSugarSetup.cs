using System.Linq.Expressions;
using System.Reflection;
using Panshi.Common.Runtime;
using Panshi.Model.Entities;
using SqlSugar;
using Yitter.IdGenerator;

namespace Panshi.Repository;

/// <summary>数据库配置（appsettings Db 节）。</summary>
public sealed class DbOptions
{
    public string ConnectionString { get; set; } = "";

    public ushort SnowflakeWorkerId { get; set; } = 1;
}

/// <summary>
/// SqlSugar PostgreSQL 上下文装配：
/// 单例 SqlSugarScope + 软删除全局过滤（逐具体实体类型注册）+ 插入/更新 AOP（雪花 Id、审计字段）。
/// </summary>
public static class SqlSugarSetup
{
    public static SqlSugarScope CreateDb(DbOptions options)
    {
        SnowflakeId.Init(options.SnowflakeWorkerId);

        var db = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = options.ConnectionString,
            DbType = DbType.PostgreSQL,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = ConfigureColumn
            }
        }, client =>
        {
            // 软删除全局过滤器：SqlSugar 泛型 AddTableFilter<T> 仅匹配「恰好是 T」的表，
            // 抽象基类 BaseEntity 不对应任何真实表 → 过滤器对具体实体全部失效。必须逐具体实体类型注册。
            foreach (var type in DbInitializer.EntityTypes)
                AddSoftDeleteFilter(client, type);

            // 单条插入/更新 AOP：填充雪花 Id 与审计字段（批量/种子路径仍需显式 NewId）
            client.Aop.DataExecuting = (oldValue, info) =>
            {
                var isInsert = info.OperationType == DataFilterType.InsertByObject;
                var isUpdate = info.OperationType == DataFilterType.UpdateByObject;
                if (!isInsert && !isUpdate) return;

                var name = info.PropertyName;
                if (Is(name, nameof(BaseEntity.Id)) && isInsert && AsLong(oldValue) == 0)
                    info.SetValue(SnowflakeId.NextId());
                else if (Is(name, nameof(BaseEntity.CreateTime)) && isInsert && UnsetDate(oldValue))
                    info.SetValue(DateTime.Now);
                else if (Is(name, nameof(BaseEntity.CreateBy)) && isInsert && AsLong(oldValue) is null or 0)
                    info.SetValue(OperationUser.UserId);
                else if (Is(name, nameof(BaseEntity.UpdateTime)) && isUpdate)
                    info.SetValue(DateTime.Now);
                else if (Is(name, nameof(BaseEntity.UpdateBy)) && isUpdate)
                    info.SetValue(OperationUser.UserId);
            };
        });
        return db;
    }

    private static void AddSoftDeleteFilter(SqlSugarClient client, Type entityType)
    {
        var param = Expression.Parameter(entityType, "it");
        var isDeleted = Expression.Property(param, nameof(BaseEntity.IsDeleted));
        var body = Expression.Equal(isDeleted, Expression.Constant(false));
        var lambda = Expression.Lambda(typeof(Func<, >).MakeGenericType(entityType, typeof(bool)), body, param);
        client.QueryFilter.AddTableFilter(entityType, lambda);
    }

    /// <summary>AOP PropertyName 可能是属性名或物理列名，两种都匹配。</summary>
    private static bool Is(string? name, string prop) =>
        name == prop || string.Equals(name, PgNaming.ToSnake(prop), StringComparison.OrdinalIgnoreCase);

    private static long? AsLong(object? v) => v switch
    {
        null => null,
        long l => l,
        _ => Convert.ToInt64(v)
    };

    /// <summary>插入实体未显式赋值的 DateTime（CLR MinValue / PG 1900 哨兵）视为待填充。</summary>
    private static bool UnsetDate(object? v) => v is not DateTime dt || dt < new DateTime(1901, 1, 1);

    /// <summary>PG 列型收敛：列名 snake_case；未显式声明时 string→text、DateTime→timestamp；可空→IsNullable。</summary>
    private static readonly NullabilityInfoContext Nullability = new();

    private static void ConfigureColumn(PropertyInfo property, EntityColumnInfo column)
    {
        column.DbColumnName = PgNaming.ToSnake(property.Name);

        var type = property.PropertyType;
        if (string.IsNullOrEmpty(column.DataType))
        {
            if (type == typeof(string)) column.DataType = "text";
            else if (type == typeof(DateTime) || type == typeof(DateTime?)) column.DataType = "timestamp";
        }

        if (column.IsNullable) return;
        var nullable = Nullable.GetUnderlyingType(type) is not null
                       || Nullability.Create(property).WriteState == NullabilityState.Nullable;
        if (nullable) column.IsNullable = true;
    }
}
