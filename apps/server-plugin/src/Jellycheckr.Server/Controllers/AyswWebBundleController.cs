using Jellycheckr.Server.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Controllers;

[ApiController]
[Route("Plugins/Aysw/web")]
[Route("Plugins/jellycheckr/web")]
[AllowAnonymous]
public sealed class AyswWebBundleController : ControllerBase
{
    private readonly ILogger<AyswWebBundleController> _logger;

    public AyswWebBundleController(ILogger<AyswWebBundleController> logger)
    {
        _logger = logger;
    }

    [HttpGet("jellycheckr-web.js")]
    public IActionResult GetWebBundle() => ServeAsset(PluginWebAssetRegistry.WebClientBundleKey, "web bundle");

    [HttpGet("jellycheckr-config-ui.js")]
    public IActionResult GetConfigUiBundle() => ServeAsset(PluginWebAssetRegistry.ConfigUiBundleKey, "config UI bundle");

    [HttpGet("jellycheckr-config-ui.css")]
    public IActionResult GetConfigUiStyles() => ServeAsset(PluginWebAssetRegistry.ConfigUiStylesKey, "config UI stylesheet");

    [HttpGet("jellycheckr-config-ui-host.html")]
    public IActionResult GetConfigUiHostPage() => ServeAsset(PluginWebAssetRegistry.ConfigUiHostPageKey, "config UI host page");

    private IActionResult ServeAsset(string assetKey, string assetLabel)
    {
        if (!PluginWebAssetRegistry.TryResolve(assetKey, out var asset, out var absolutePath))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Web asset key was not registered: {AssetKey}", assetKey);
            return NotFound();
        }

        if (!System.IO.File.Exists(absolutePath))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Plugin web asset not found: {AssetLabel} at {AssetPath}",
                assetLabel,
                absolutePath);
            return NotFound();
        }

        Response.Headers.CacheControl = asset.CacheControl;
        return PhysicalFile(absolutePath, asset.ContentType);
    }
}

