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
    public IActionResult GetWebBundle()
    {
        if (!EmbeddedWebClientBundle.TryGetBundle(out var script))
        {
            _logger.LogWarning("[Jellycheckr] Embedded web bundle not found.");
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=3600";
        return Content(script, "application/javascript; charset=utf-8");
    }

    [HttpGet("jellycheckr-config-ui.js")]
    public IActionResult GetConfigUiBundle()
    {
        if (!EmbeddedConfigUiBundle.TryGetBundle(out var script))
        {
            _logger.LogWarning("[Jellycheckr] Embedded config UI bundle not found.");
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=300";
        return Content(script, "application/javascript; charset=utf-8");
    }

    [HttpGet("jellycheckr-config-ui-host.html")]
    public IActionResult GetConfigUiHostPage()
    {
        if (!EmbeddedConfigUiHostPage.TryGetHtml(out var html))
        {
            _logger.LogWarning("[Jellycheckr] Embedded config UI host page not found.");
            return NotFound();
        }

        Response.Headers.CacheControl = "public,max-age=300";
        return Content(html, "text/html; charset=utf-8");
    }
}
