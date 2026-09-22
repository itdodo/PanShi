namespace Panshi.Common.Results;

/// <summary>统一结果封装：code=0 成功；非 0 为业务错误码（HTTP 状态由过滤器/异常映射决定）。</summary>
public class ApiResult
{
    public int Code { get; set; }

    public string Msg { get; set; } = "ok";

    public static ApiResult Ok() => new();

    public static ApiResult<T> Ok<T>(T data, string msg = "ok") => new() { Data = data, Msg = msg };

    public static ApiResult Fail(int code, string msg) => new() { Code = code, Msg = msg };
}

/// <summary>带数据的结果封装。Data 为 null 时序列化为 null（前端按 code 判断）。</summary>
public class ApiResult<T> : ApiResult
{
    public T? Data { get; set; }
}

/// <summary>分页结果（rows 前端表格数据源，total 总数）。默认排序 CreateTime desc。</summary>
public class PagedResult<T>
{
    public long Total { get; set; }

    public IReadOnlyList<T> Rows { get; set; } = Array.Empty<T>();
}
