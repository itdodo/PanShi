using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Panshi.Common.Runtime;
using Panshi.Common.Security;
using Panshi.Model.Entities;
using SqlSugar;

namespace Panshi.Api.Filters;

/// <summary>
/// 操作日志全局过滤器（蓝图§5.5）：自动记录全部 POST/PUT/DELETE/PATCH；
/// 参数脱敏（password/secret/token/credential 词根）+ 耗时 + IP + 成败/错误。
/// 登录接口不记（由 sys_login_log 专表）。
/// </summary>
public class OperationLogFilter(ISqlSugarClient db) : IAsyncActionFilter
{
    private static readonly string[] TrackedMethods = ["POST", "PUT", "DELETE", "PATCH"];

    private const string StartKey = "__oplog_start";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!TrackedMethods.Contains(request.Method) || request.Path.StartsWithSegments("/api/v1/auth"))
        {
            await next();
            return;
        }

        var sw = Stopwatch.StartNew();
        context.HttpContext.Items[StartKey] = sw;
        var executed = await next();

        sw.Stop();
        try
        {
            var (module, action) = Describe(context.ActionDescriptor);
            var log = new SysOperationLog
            {
                Id = Repository.SnowflakeId.NextId(),
                Module = module,
                Action = action,
                Method = request.Method,
                Url = request.Path + request.QueryString,
                Params = CaptureParams(context, request),
                UserId = OperationUser.UserId,
                UserName = OperationUser.UserName,
                Ip = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = request.Headers.UserAgent.ToString(),
                ElapsedMs = sw.ElapsedMilliseconds,
                Success = executed.Exception is null,
                ErrorMsg = executed.Exception is null ? null : SensitiveData.Truncate(executed.Exception.Message, 2000),
                CreateTime = DateTime.Now,
                CreateBy = OperationUser.UserId
            };
            await db.Insertable(log).ExecuteCommandAsync();
        }
        catch
        {
            // 日志失败不影响业务响应
        }
    }

    private static (string, string) Describe(Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor descriptor)
    {
        if (descriptor is ControllerActionDescriptor cad)
        {
            var tags = cad.ControllerTypeInfo.GetCustomAttributes(typeof(TagsAttribute), true)
                .Cast<TagsAttribute>().FirstOrDefault()?.Tags;
            var module = tags?.FirstOrDefault() ?? cad.ControllerName;
            return (module.Trim('[', ']'), cad.ActionName);
        }

        return ("unknown", "unknown");
    }

    private static string? CaptureParams(ActionExecutingContext context, HttpRequest request)
    {
        // 用已绑定参数（模型绑定后稳定可得，避免重读流失败）；含 IFormFile 的按 multipart 处理
        if (context.ActionArguments.Values.Any(a => a is IFormFile))
            return $"[multipart files={context.ActionArguments.Values.OfType<IFormFile>().Count()}]";
        if (context.ActionArguments.Count > 0)
        {
            var json = SafeSerialize(context.ActionArguments);
            return string.IsNullOrEmpty(json) ? null : SensitiveData.Truncate(SensitiveData.MaskJson(json));
        }

        var qs = request.QueryString.Value;
        return string.IsNullOrWhiteSpace(qs) ? null : SensitiveData.Truncate(qs);
    }

    private static string SafeSerialize(System.Collections.Generic.IDictionary<string, object?> args)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(
                args.Where(kv => kv.Value is not IFormFile).ToDictionary(kv => kv.Key, kv => kv.Value),
                Common.Json.JsonConfig.Options);
        }
        catch
        {
            return string.Empty;
        }
    }
}
