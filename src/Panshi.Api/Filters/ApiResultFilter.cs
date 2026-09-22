using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Panshi.Common.Results;

namespace Panshi.Api.Filters;

/// <summary>
/// 统一响应包装：非 ApiResult/文件类结果自动包成 {code:0,msg:"ok",data}；
/// HTTP 状态保持 200，业务码走 code（401/403 由认证事件直写，异常由 ExceptionMiddleware 直写）。
/// </summary>
public class ApiResultFilter : IResultFilter
{
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        switch (context.Result)
        {
            case ObjectResult { Value: null } obj when obj.DeclaredType == typeof(ApiResult):
                context.Result = new ObjectResult(ApiResult.Ok()) { StatusCode = StatusCodes.Status200OK };
                return;
            case ObjectResult { Value: ApiResult }:
                return;
            case EmptyResult:
                context.Result = new ObjectResult(ApiResult.Ok()) { StatusCode = StatusCodes.Status200OK };
                return;
            case ObjectResult obj:
                context.Result = new ObjectResult(ApiResult.Ok(obj.Value)) { StatusCode = obj.StatusCode };
                return;
            case StatusCodeResult { StatusCode: 200 } scr:
                context.Result = new ObjectResult(ApiResult.Ok()) { StatusCode = scr.StatusCode };
                return;
        }
    }
}
