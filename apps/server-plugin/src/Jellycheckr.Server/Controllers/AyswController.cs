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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var effectiveConfig = _configService.GetEffectiveConfig(userId);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            effectiveConfig,
            "GET /config userId={UserId} effectiveConfig={@EffectiveConfig}",
            userId ?? "(null)",
            JellycheckrDiagnosticLogging.Describe(effectiveConfig));
        return Ok(effectiveConfig);
    }

    [HttpPost("sessions/{sessionId}/ack")]
    public ActionResult<AckResponse> Ack(string sessionId, [FromBody] AckRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var config = _configService.GetEffectiveConfig(userId);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
            "POST /sessions/{SessionId}/ack userId={UserId} request={@Request}",
            sessionId,
            userId ?? "(null)",
            request);
        var response = _ackService.HandleAck(sessionId, request, config);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
            "POST /sessions/{SessionId}/ack response={@Response}",
            sessionId,
            response);
        return Ok(response);
    }

    [HttpPost("sessions/{sessionId}/interaction")]
    public ActionResult<InteractionResponse> Interaction(string sessionId, [FromBody] InteractionRequest request)
    {
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "POST /sessions/{SessionId}/interaction request={@Request}",
            sessionId,
            request);
        var response = _ackService.HandleInteraction(sessionId, request);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "POST /sessions/{SessionId}/interaction response={@Response}",
            sessionId,
            response);
        return Ok(response);
    }

    [HttpPost("sessions/{sessionId}/prompt-shown")]
    public IActionResult PromptShown(string sessionId, [FromBody] PromptShownRequest request)
    {
        var effectiveTimeout = Math.Max(request.TimeoutSeconds, 10);
        var deadline = _clock.UtcNow.AddSeconds(effectiveTimeout);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "POST /sessions/{SessionId}/prompt-shown request={@Request} effectiveTimeout={EffectiveTimeout} deadlineUtc={DeadlineUtc}",
            sessionId,
            request,
            effectiveTimeout,
            deadline);
        _ackService.MarkPromptActive(sessionId, deadline);
        return Accepted();
    }
}
