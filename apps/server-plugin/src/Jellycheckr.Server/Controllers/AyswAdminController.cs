using Jellycheckr.Server.Models;
using Jellycheckr.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellycheckr.Server.Infrastructure;

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
            _logger.LogWarning("[Jellycheckr] Admin config GET forbidden for user {User}.", User.Identity?.Name ?? "(unknown)");
            return Forbid();
        }

        var config = _configService.GetAdminConfig();
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
            "GET /admin/config user={User} config={@Config}",
            User.Identity?.Name ?? "(unknown)",
            JellycheckrDiagnosticLogging.Describe(config));
        return Ok(config);
    }

    [HttpPut("config")]
    public ActionResult<PluginConfig> PutAdminConfig([FromBody] PluginConfig config)
    {
        if (!IsAdmin())
        {
            _logger.LogWarning("[Jellycheckr] Admin config PUT forbidden for user {User}.", User.Identity?.Name ?? "(unknown)");
            return Forbid();
        }

        try
        {
            _logger.LogInformation("[Jellycheckr] Admin config update requested by {User}.", User.Identity?.Name ?? "(unknown)");
            JellycheckrDiagnosticLogging.Verbose(
                _logger,
                config,
                "PUT /admin/config payload={@Config}",
                JellycheckrDiagnosticLogging.Describe(config));

            var updated = _configService.UpdateAdminConfig(config);
            JellycheckrDiagnosticLogging.Verbose(
                _logger,
                updated,
                "PUT /admin/config persistedConfig={@Config}",
                JellycheckrDiagnosticLogging.Describe(updated));
            return Ok(updated);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "[Jellycheckr] Admin config validation failed.");
            return BadRequest(new { error = ex.Message });
        }
    }

    private bool IsAdmin()
    {
        return User.IsInRole("Administrator")
               || User.Claims.Any(c => c.Type.EndsWith("role", StringComparison.OrdinalIgnoreCase)
                                       && c.Value.Equals("Administrator", StringComparison.OrdinalIgnoreCase));
    }
}
