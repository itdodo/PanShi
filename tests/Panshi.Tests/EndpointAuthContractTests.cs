using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Panshi.Api.Authorization;
using Panshi.Api.Controllers.System;
using Xunit;

namespace Panshi.Tests;

/// <summary>
/// 端点授权契约。授权弹窗要的只是菜单结构，不是菜单管理权：
/// 若它走 sys:menu:list，则「有 sys:role:* 却没有菜单权限」的客服/用户管理员打开弹窗是 403 + 空树。
/// </summary>
public class EndpointAuthContractTests
{
    /// <summary>只取方法级 [Authorize]/[HasPermission] 的策略名；没有则为 null（=控制器级「登录即可」）。</summary>
    private static string? MethodPolicy(Type controller, string action) =>
        controller.GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>(inherit: false)?.Policy;

    [Fact]
    public void GrantTree_And_MyTree_Are_Login_Only_FullTree_Keeps_Permission()
    {
        Assert.Null(MethodPolicy(typeof(MenuController), nameof(MenuController.GrantTree)));
        Assert.Null(MethodPolicy(typeof(MenuController), nameof(MenuController.MyTree)));
        Assert.Equal(PermPolicyNames.For("sys:menu:list"),
            MethodPolicy(typeof(MenuController), nameof(MenuController.Tree)));
    }

    [Fact]
    public void Menu_Controller_Still_Requires_Authentication()
    {
        var controllerLevel = typeof(MenuController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(controllerLevel);
        Assert.Null(controllerLevel!.Policy);
    }
}
