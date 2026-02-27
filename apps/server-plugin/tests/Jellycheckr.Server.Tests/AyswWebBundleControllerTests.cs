using Jellycheckr.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class AyswWebBundleControllerTests
{
    [Fact]
    public void GetWebBundle_ReturnsPhysicalFile_WhenAssetExists()
    {
        EnsureTestAssets();
        var controller = CreateController();

        var result = controller.GetWebBundle();

        var fileResult = Assert.IsType<PhysicalFileResult>(result);
        Assert.True(File.Exists(fileResult.FileName));
        Assert.Equal("application/javascript; charset=utf-8", fileResult.ContentType);
        Assert.Equal("public,max-age=3600", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void GetConfigUiBundle_ReturnsPhysicalFile_WhenAssetExists()
    {
        EnsureTestAssets();
        var controller = CreateController();

        var result = controller.GetConfigUiBundle();

        var fileResult = Assert.IsType<PhysicalFileResult>(result);
        Assert.True(File.Exists(fileResult.FileName));
        Assert.Equal("application/javascript; charset=utf-8", fileResult.ContentType);
        Assert.Equal("public,max-age=300", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void GetConfigUiStyles_ReturnsPhysicalFile_WhenAssetExists()
    {
        EnsureTestAssets();
        var controller = CreateController();

        var result = controller.GetConfigUiStyles();

        var fileResult = Assert.IsType<PhysicalFileResult>(result);
        Assert.True(File.Exists(fileResult.FileName));
        Assert.Equal("text/css; charset=utf-8", fileResult.ContentType);
        Assert.Equal("public,max-age=300", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void GetConfigUiHostPage_ReturnsPhysicalFile_WhenAssetExists()
    {
        EnsureTestAssets();
        var controller = CreateController();

        var result = controller.GetConfigUiHostPage();

        var fileResult = Assert.IsType<PhysicalFileResult>(result);
        Assert.True(File.Exists(fileResult.FileName));
        Assert.Equal("text/html; charset=utf-8", fileResult.ContentType);
        Assert.Equal("public,max-age=300", controller.Response.Headers.CacheControl.ToString());
    }

    private static AyswWebBundleController CreateController()
    {
        return new AyswWebBundleController(NullLogger<AyswWebBundleController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static void EnsureTestAssets()
    {
        var webRoot = Path.Combine(Path.GetDirectoryName(typeof(AyswWebBundleController).Assembly.Location)!, "web");
        Directory.CreateDirectory(webRoot);

        File.WriteAllText(Path.Combine(webRoot, "jellycheckr-web.js"), "console.log('web');");
        File.WriteAllText(Path.Combine(webRoot, "jellycheckr-config-ui.js"), "console.log('config-ui');");
        File.WriteAllText(Path.Combine(webRoot, "jellycheckr-config-ui.css"), "body{}");
        File.WriteAllText(Path.Combine(webRoot, "jellycheckr-config-ui-host.html"), "<html><body>host</body></html>");
    }
}
