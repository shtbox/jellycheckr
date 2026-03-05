using System.Security.Claims;
using Jellycheckr.Server.Controllers;
using Jellycheckr.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class AyswAdminControllerTests
{
    [Fact]
    public void GetAdminConfig_AllowsExactRoleClaimType()
    {
        var controller = CreateController(new[]
        {
            new Claim(ClaimTypes.Role, "Administrator")
        });

        var result = controller.GetAdminConfig();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<PluginConfig>(ok.Value);
    }

    [Fact]
    public void GetAdminConfig_RejectsSuffixRoleClaimType()
    {
        var controller = CreateController(new[]
        {
            new Claim("customrole", "Administrator")
        });

        var result = controller.GetAdminConfig();

        Assert.IsType<ForbidResult>(result.Result);
    }

    private static AyswAdminController CreateController(IEnumerable<Claim> claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return new AyswAdminController(new StubConfigService(), NullLogger<AyswAdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal
                }
            }
        };
    }
}
