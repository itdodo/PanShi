using Microsoft.AspNetCore.Mvc.Filters;
using Panshi.Common.Cache;
using Panshi.Common.Exceptions;
using Panshi.Common.Runtime;

namespace Panshi.Api.Filters;

/// <summary>幂等：用户+路径+参数哈希 2 秒窗口内拒绝重复提交（安全清单 #7）。</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class NoRepeatSubmitAttribute(int windowSeconds = 2) : Attribute
{
    public int WindowSeconds { get; } = windowSeconds;
}

public class NoRepeatSubmitFilter(ICacheService cache) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attr = context.ActionDescriptor.EndpointMetadata.OfType<NoRepeatSubmitAttribute>().FirstOrDefault();
        if (attr is null)
        {
            await next();
            return;
        }

        var request = context.HttpContext.Request;
        var hash = ComputeHash(request.Method, request.Path, request.QueryString.HasValue ? request.QueryString.Value ?? "" : "", context.ActionArguments);
        var key = $"norepeat:{OperationUser.UserId}:{hash}";
        if (cache.Get<string>(key) is not null)
            throw new BizException("请勿重复提交，请稍候再试", 429);

        cache.Set(key, "1", TimeSpan.FromSeconds(attr.WindowSeconds));
        await next();
    }

    private static string ComputeHash(string method, string path, string query, IDictionary<string, object?> args)
    {
        var payload = method + path + query + Safe(args);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payload)))[..24];

        static string Safe(IDictionary<string, object?> a)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(a, Common.Json.JsonConfig.Options);
            }
            catch
            {
                return string.Join(",", a.Keys);
            }
        }
    }
}
