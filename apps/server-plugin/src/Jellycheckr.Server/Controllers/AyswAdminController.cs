using Jellycheckr.Server.Models;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Controllers;

[ApiController]
[Route("Plugins/Aysw/admin")]
[Route("Plugins/jellycheckr/admin")]
[Authorize]
public sealed class AyswAdminController : ControllerBase
{
    private readonly IConfigService _configService;
    private readonly ILogger<AyswAdminController> _logger;

    public AyswAdminController(IConfigService configService, ILogger<AyswAdminController> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    [HttpGet("config")]
    public ActionResult<PluginConfig> GetAdminConfig()
    {
        if (!IsAdmin())
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Admin config GET forbidden for user {User}.", User.Identity?.Name ?? "(unknown)");
            return Forbid();
        }

        try
        {
            var config = _configService.GetAdminConfig();
            _logger.LogJellycheckrTrace(
                "GET /admin/config user={User} config={@Config}",
                User.Identity?.Name ?? "(unknown)",
                config);
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to load admin config for user {User}.", User.Identity?.Name ?? "(unknown)");
            return StatusCode(500, new { error = "Failed to load admin config." });
        }
    }

    [HttpPut("config")]
    public ActionResult<PluginConfig> PutAdminConfig([FromBody] PluginConfig config)
    {
        if (!IsAdmin())
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Admin config PUT forbidden for user {User}.", User.Identity?.Name ?? "(unknown)");
            return Forbid();
        }

        try
        {
            _logger.LogJellycheckrTrace("[Jellycheckr] Admin config update requested by {User}.", User.Identity?.Name ?? "(unknown)");
            _logger.LogJellycheckrTrace("PUT /admin/config payload={@Config}", config);

            var updated = _configService.UpdateAdminConfig(config);
            _logger.LogJellycheckrTrace("PUT /admin/config persistedConfig={@Config}", updated);
            return Ok(updated);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Admin config validation failed.");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to persist admin config for user {User}.", User.Identity?.Name ?? "(unknown)");
            return StatusCode(500, new { error = "Failed to persist admin config." });
        }
    }

    private bool IsAdmin()
    {
        return User.IsInRole("Administrator")
               || User.Claims.Any(c => c.Type.EndsWith("role", StringComparison.OrdinalIgnoreCase)
                                       && c.Value.Equals("Administrator", StringComparison.OrdinalIgnoreCase));
    }
}

