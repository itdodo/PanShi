using Microsoft.AspNetCore.Http;
using Panshi.Api.Middleware;
using Panshi.Common.Security;
using Xunit;

namespace Panshi.Tests;

/// <summary>
/// 认证边界的两条纯逻辑契约：
/// ① 强制改密时服务端只放行改密页要用的那几条端点（此前只有前端路由拦，手搓请求就绕过）；
/// ② 改密请求体进操作日志前必须被脱敏（此前 /api/v1/auth 整段不记，所以从没暴露过）。
/// </summary>
public class AuthBoundaryTests
{
    private static PathString Path(string p) => new(p);

    [Theory]
    [InlineData("/api/v1/auth/change-password")]
    [InlineData("/api/v1/auth/change-password/extra")]
    [InlineData("/api/v1/auth/profile")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/logout")]
    public void 密码被强制更换时_改密页要用的端点放行(string allowed)
        => Assert.True(PasswordGate.Allows(Path(allowed)));

    [Theory]
    // 业务端点一律挡掉
    [InlineData("/api/v1/sys/user")]
    [InlineData("/api/v1/scm/stock-doc/page")]
    [InlineData("/api/v1/flow/task/1/act")]
    // 同前缀但不是那条端点：StartsWithSegments 的边界，用 StartsWith 就会漏放行
    [InlineData("/api/v1/auth/profiles")]
    [InlineData("/api/v1/auth/logout-everywhere")]
    // 会话管理不在白名单里：口令还没换的人不该能改自己的会话
    [InlineData("/api/v1/auth/sessions")]
    [InlineData("/api/v1/auth/avatar")]
    public void 密码被强制更换时_其余端点全部拒绝(string blocked)
        => Assert.False(PasswordGate.Allows(Path(blocked)));

    [Fact]
    public void 改密请求体落日志前_新旧口令都已脱敏()
    {
        var masked = SensitiveData.MaskJson("""{"dto":{"oldPassword":"Abc@123456","newPassword":"New@Pass123"}}""");
        Assert.DoesNotContain("Abc@123456", masked);
        Assert.DoesNotContain("New@Pass123", masked);
        Assert.Contains("\"oldPassword\":\"******\"", masked);
        Assert.Contains("\"newPassword\":\"******\"", masked);
    }
}
