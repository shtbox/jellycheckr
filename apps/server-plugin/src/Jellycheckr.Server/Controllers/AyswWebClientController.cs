using System.Security.Claims;
using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Controllers;

[ApiController]
[Route("Plugins/Aysw/web-client")]
[Route("Plugins/jellycheckr/web-client")]
[Authorize]
public sealed class AyswWebClientController : ControllerBase
{
    private readonly IWebClientRegistrationService _registrationService;
    private readonly ILogger<AyswWebClientController> _logger;

    public AyswWebClientController(
        IWebClientRegistrationService registrationService,
        ILogger<AyswWebClientController> logger)
    {
        _registrationService = registrationService;
        _logger = logger;
    }

    [HttpPost("register")]
    public ActionResult<WebClientRegisterResponse> Register([FromBody] WebClientRegisterRequest request)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogJellycheckrTrace(
                "POST /web-client/register userId={UserId} request={@Request}",
                userId ?? "(null)",
                request);
            var response = _registrationService.Register(userId, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Invalid web client register request.");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to register a web client session.");
            return StatusCode(500, new { error = "Failed to register web client session." });
        }
    }

    [HttpPost("heartbeat")]
    public ActionResult<WebClientHeartbeatResponse> Heartbeat([FromBody] WebClientHeartbeatRequest request)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogJellycheckrTrace(
                "POST /web-client/heartbeat userId={UserId} request={@Request}",
                userId ?? "(null)",
                request);
            var response = _registrationService.Heartbeat(userId, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Invalid web client heartbeat request.");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to renew a web client session registration.");
            return StatusCode(500, new { error = "Failed to renew web client session registration." });
        }
    }

    [HttpPost("unregister")]
    public IActionResult Unregister([FromBody] WebClientUnregisterRequest request)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogJellycheckrTrace(
                "POST /web-client/unregister userId={UserId} request={@Request}",
                userId ?? "(null)",
                request);
            _registrationService.Unregister(userId, request);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Invalid web client unregister request.");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to unregister a web client session.");
            return StatusCode(500, new { error = "Failed to unregister web client session." });
        }
    }
}
