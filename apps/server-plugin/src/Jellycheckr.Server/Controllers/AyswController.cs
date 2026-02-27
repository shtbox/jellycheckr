using System.Security.Claims;
using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Controllers;

[ApiController]
[Route("Plugins/Aysw")]
[Route("Plugins/jellycheckr")]
[Authorize]
public sealed class AyswController : ControllerBase
{
    private readonly IConfigService _configService;
    private readonly IAckService _ackService;
    private readonly IClock _clock;
    private readonly ILogger<AyswController> _logger;

    public AyswController(IConfigService configService, IAckService ackService, IClock clock, ILogger<AyswController> logger)
    {
        _configService = configService;
        _ackService = ackService;
        _clock = clock;
        _logger = logger;
    }

    [HttpGet("config")]
    public ActionResult<EffectiveConfigResponse> GetConfig()
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var effectiveConfig = _configService.GetEffectiveConfig(userId);
            _logger.LogJellycheckrTrace(
                "GET /config userId={UserId} effectiveConfig={@EffectiveConfig}",
                userId ?? "(null)",
                effectiveConfig);
            return Ok(effectiveConfig);
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to resolve effective config.");
            return StatusCode(500, new { error = "Failed to load effective config." });
        }
    }

    [HttpPost("sessions/{sessionId}/ack")]
    public ActionResult<AckResponse> Ack(string sessionId, [FromBody] AckRequest request)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var config = _configService.GetEffectiveConfig(userId);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/ack userId={UserId} request={@Request}",
                sessionId,
                userId ?? "(null)",
                request);
            var response = _ackService.HandleAck(sessionId, request, config);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/ack response={@Response}",
                sessionId,
                response);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Invalid ack request for session={SessionId}.", sessionId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to process ack for session={SessionId}.", sessionId);
            return StatusCode(500, new { error = "Failed to process ack request." });
        }
    }

    [HttpPost("sessions/{sessionId}/interaction")]
    public ActionResult<InteractionResponse> Interaction(string sessionId, [FromBody] InteractionRequest request)
    {
        try
        {
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/interaction request={@Request}",
                sessionId,
                request);
            var response = _ackService.HandleInteraction(sessionId, request);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/interaction response={@Response}",
                sessionId,
                response);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Invalid interaction request for session={SessionId}.", sessionId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to process interaction for session={SessionId}.", sessionId);
            return StatusCode(500, new { error = "Failed to process interaction request." });
        }
    }

    [HttpPost("sessions/{sessionId}/prompt-shown")]
    public IActionResult PromptShown(string sessionId, [FromBody] PromptShownRequest request)
    {
        try
        {
            var effectiveTimeout = Math.Max(request.TimeoutSeconds, 10);
            var deadline = _clock.UtcNow.AddSeconds(effectiveTimeout);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/prompt-shown request={@Request} effectiveTimeout={EffectiveTimeout} deadlineUtc={DeadlineUtc}",
                sessionId,
                request,
                effectiveTimeout,
                deadline);
            _ackService.MarkPromptActive(sessionId, deadline);
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Prompt decision session={SessionId} action=popup_shown itemId={ItemId} timeoutSeconds={TimeoutSeconds} deadlineUtc={DeadlineUtc}.",
                sessionId,
                request.ItemId ?? "(none)",
                effectiveTimeout,
                deadline);
            return Accepted();
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Invalid prompt-shown request for session={SessionId}.", sessionId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Failed to process prompt-shown for session={SessionId}.", sessionId);
            return StatusCode(500, new { error = "Failed to process prompt-shown request." });
        }
    }
}

