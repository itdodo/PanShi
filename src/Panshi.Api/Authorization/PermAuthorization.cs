using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Panshi.Service.Sys;

namespace Panshi.Api.Authorization;

/// <summary>权限码声明（Controller/Action 上 [HasPermission("sys:user:add")]）。策略名动态生成。</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class HasPermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "perm:";

    public HasPermissionAttribute(string code)
    {
        Code = code;
        Policy = Prefix + code;
    }

    public string Code { get; }
}

/// <summary>动态策略名工厂：perm: 前缀策略由 PermPolicyProvider 实时生成。</summary>
public static class PermPolicyNames
{
    public static string For(string code) => HasPermissionAttribute.Prefix + code;
}

public sealed class PermRequirement(string code) : IAuthorizationRequirement
{
    public string Code { get; } = code;
}

/// <summary>处理器：比对用户权限集合（进程缓存，版本号整体失效）。</summary>
public class PermAuthorizationHandler(PermissionService permissions)
    : AuthorizationHandler<PermRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermRequirement requirement)
    {
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sub is null || !long.TryParse(sub, out var userId)) return;

        if (await permissions.HasPermissionAsync(userId, requirement.Code))
            context.Succeed(requirement);
    }
}

/// <summary>动态策略提供器：任意 perm:xxx 策略实时构造，无需注册。</summary>
public class PermPolicyProvider(IServiceProvider services) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>());

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.Prefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var code = policyName[HasPermissionAttribute.Prefix.Length..];
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermRequirement(code))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
