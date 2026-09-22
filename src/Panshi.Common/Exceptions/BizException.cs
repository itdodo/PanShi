namespace Panshi.Common.Exceptions;

/// <summary>业务异常：被全局异常中间件捕获后按 Code 返回统一结果（默认 400 语义的业务码）。</summary>
public class BizException : Exception
{
    public int Code { get; }

    public BizException(string msg, int code = 1) : base(msg) => Code = code;

    /// <summary>资源不存在</summary>
    public static BizException NotFound(string what = "数据") => new($"{what}不存在或已被删除", 404);

    /// <summary>未认证/令牌失效</summary>
    public static BizException Unauthorized(string msg = "登录状态已失效，请重新登录") => new(msg, 401);

    /// <summary>无权限</summary>
    public static BizException Forbidden(string msg = "没有权限执行此操作") => new(msg, 403);

    /// <summary>并发冲突（乐观锁）</summary>
    public static BizException Conflict(string msg = "数据已被他人修改，请刷新后重试") => new(msg, 409);
}
