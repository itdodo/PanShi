using System.Security.Claims;
using System.Text.Json;
using Panshi.Common.Results;
using Panshi.Common.Json;
using Panshi.Model.Entities;
using Panshi.Service.Sys;

namespace Panshi.Api.Middleware;

/// <summary>
/// JWT 会话校验 + 操作者上下文：
/// OnTokenValidated 按 sys_user_session 校验存在性/有效期/用户状态 → 登出/踢线/停用即时生效；
/// 通过后写 AsyncLocal OperationUser 供 AOP/日志/审计取用。
/// </summary>
public static class AuthSetup
{
    public static void AddPanshiAuth(this IServiceCollection services, IConfiguration config)
    {
        var authOptions = config.GetSection("Jwt").Get<Service.Auth.AuthOptions>()
                          ?? throw new InvalidOperationException("缺少 Jwt 配置节");
        authOptions.Validate();
        services.AddSingleton(authOptions);
        services.AddSingleton<Service.Auth.TokenService>();

        services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = authOptions.Issuer,
                    ValidAudience = authOptions.Audience,
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(authOptions.SecretKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.NameIdentifier
                };

                options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var jti = ctx.Principal?.FindFirstValue("jti");
                        var sub = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (jti is null || sub is null || !long.TryParse(sub, out var userId))
                        {
                            ctx.Fail("令牌声明缺失");
                            return;
                        }

                        var auth = ctx.HttpContext.RequestServices.GetRequiredService<AuthService>();
                        var user = await auth.ValidateSessionAsync(jti, userId);
                        if (user is null)
                        {
                            ctx.Fail("会话不存在或已失效");
                            return;
                        }

                        ctx.HttpContext.Items["SessionUser"] = user;
                    },
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        if (ctx.Response.HasStarted) return;
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.Response.ContentType = "application/json; charset=utf-8";
                        await ctx.Response.WriteAsync(JsonSerializer.Serialize(
                            ApiResult.Fail(401, "登录状态已失效，请重新登录"), JsonConfig.Options));
                    },
                    OnForbidden = async ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        ctx.Response.ContentType = "application/json; charset=utf-8";
                        await ctx.Response.WriteAsync(JsonSerializer.Serialize(
                            ApiResult.Fail(403, "没有权限执行此操作"), JsonConfig.Options));
                    }
                };

                // SignalR WebSocket 握手：accessToken 走查询串
                options.Events.OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) && ctx.Request.Path.StartsWithSegments("/hubs"))
                        ctx.Token = accessToken;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
    }

    public static ClaimsPrincipal? CurrentUser(this HttpContext ctx) => ctx.User;

    public static long CurrentUserId(this HttpContext ctx) =>
        long.TryParse(ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    /// <summary>
    /// 当前用户显示名（真名/昵称），用于落 owner_user_name / submitter_name 这类展示性冗余列。
    /// ⚠️ 不要用 User.Identity?.Name：本文件把 NameClaimType 指到了 NameIdentifier，
    /// 那里拿到的是用户 id（曾导致报销单「申请人」列显示成一串雪花号）。
    /// </summary>
    public static string CurrentDisplayName(this HttpContext ctx) =>
        ctx.User?.FindFirstValue("nick") ?? ctx.User?.FindFirstValue(ClaimTypes.Name) ?? "";

    public static string? CurrentTokenId(this HttpContext ctx) =>
        ctx.User?.FindFirstValue("jti");
}

/// <summary>从声明取部门 Id（OperationUser 中间件用）。</summary>
public static class AuthClaimTypes
{
    public const string Dept = "dept";
}
