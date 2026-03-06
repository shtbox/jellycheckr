using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    private readonly ISessionOwnershipValidator _sessionOwnershipValidator;
    private readonly IAuthenticatedUserIdResolver _authenticatedUserIdResolver;
    private readonly IClock _clock;
    private readonly ILogger<AyswController> _logger;

    public AyswController(
        IConfigService configService,
        IAckService ackService,
        ISessionOwnershipValidator sessionOwnershipValidator,
        IAuthenticatedUserIdResolver authenticatedUserIdResolver,
        IClock clock,
        ILogger<AyswController> logger)
    {
        _configService = configService;
        _ackService = ackService;
        _sessionOwnershipValidator = sessionOwnershipValidator;
        _authenticatedUserIdResolver = authenticatedUserIdResolver;
        _clock = clock;
        _logger = logger;
    }

    [HttpGet("config")]
    public ActionResult<EffectiveConfigResponse> GetConfig()
    {
        try
        {
            var userId = _authenticatedUserIdResolver.Resolve(HttpContext);
            var effectiveConfig = _configService.GetEffectiveConfig(userId);
            _logger.LogJellycheckrTrace(
                "GET /config userId={UserId} enabled={Enabled} fallbackEnabled={FallbackEnabled}",
                JellycheckrLogSanitizer.RedactIdentifier(userId),
                effectiveConfig.Enabled,
                effectiveConfig.EnableServerFallback);
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
            var userId = _authenticatedUserIdResolver.Resolve(HttpContext);
            if (!_sessionOwnershipValidator.CanMutateSession(userId, sessionId))
            {
                _logger.LogJellycheckrWarning(
                    "[Jellycheckr] Forbidden ack mutation session={SessionId} userId={UserId}.",
                    JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                    JellycheckrLogSanitizer.RedactIdentifier(userId));
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
            }

            var config = _configService.GetEffectiveConfig(userId);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/ack userId={UserId} ackType={AckType} clientType={ClientType}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                JellycheckrLogSanitizer.RedactIdentifier(userId),
                request.AckType,
                request.ClientType ?? "(none)");
            var response = _ackService.HandleAck(sessionId, userId, request, config);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/ack resetApplied={ResetApplied}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                response.ResetApplied);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Forbidden ack mutation.");
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(
                ex,
                "[Jellycheckr] Invalid ack request for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(
                ex,
                "[Jellycheckr] Failed to process ack for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            return StatusCode(500, new { error = "Failed to process ack request." });
        }
    }

    [HttpPost("sessions/{sessionId}/interaction")]
    public ActionResult<InteractionResponse> Interaction(string sessionId, [FromBody] InteractionRequest request)
    {
        try
        {
            var userId = _authenticatedUserIdResolver.Resolve(HttpContext);
            if (!_sessionOwnershipValidator.CanMutateSession(userId, sessionId))
            {
                _logger.LogJellycheckrWarning(
                    "[Jellycheckr] Forbidden interaction mutation session={SessionId} userId={UserId}.",
                    JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                    JellycheckrLogSanitizer.RedactIdentifier(userId));
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
            }

            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/interaction eventType={EventType} clientType={ClientType}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                request.EventType,
                request.ClientType ?? "(none)");
            var response = _ackService.HandleInteraction(sessionId, userId, request);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/interaction accepted={Accepted}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                response.Accepted);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Forbidden interaction mutation.");
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(
                ex,
                "[Jellycheckr] Invalid interaction request for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(
                ex,
                "[Jellycheckr] Failed to process interaction for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            return StatusCode(500, new { error = "Failed to process interaction request." });
        }
    }

    [HttpPost("sessions/{sessionId}/prompt-shown")]
    public IActionResult PromptShown(string sessionId, [FromBody] PromptShownRequest request)
    {
        try
        {
            var userId = _authenticatedUserIdResolver.Resolve(HttpContext);
            if (!_sessionOwnershipValidator.CanMutateSession(userId, sessionId))
            {
                _logger.LogJellycheckrWarning(
                    "[Jellycheckr] Forbidden prompt-shown mutation session={SessionId} userId={UserId}.",
                    JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                    JellycheckrLogSanitizer.RedactIdentifier(userId));
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
            }

            var effectiveTimeout = Math.Max(request.TimeoutSeconds, 10);
            var deadline = _clock.UtcNow.AddSeconds(effectiveTimeout);
            _logger.LogJellycheckrTrace(
                "POST /sessions/{SessionId}/prompt-shown clientType={ClientType} timeoutSeconds={TimeoutSeconds} deadlineUtc={DeadlineUtc}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                request.ClientType ?? "(none)",
                effectiveTimeout,
                deadline);
            _ackService.MarkPromptActive(sessionId, userId, deadline, request.ClientType);
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Prompt decision session={SessionId} action=popup_shown itemId={ItemId} timeoutSeconds={TimeoutSeconds} deadlineUtc={DeadlineUtc}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                JellycheckrLogSanitizer.RedactIdentifier(request.ItemId),
                effectiveTimeout,
                deadline);
            return Accepted();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Forbidden prompt-shown mutation.");
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(
                ex,
                "[Jellycheckr] Invalid prompt-shown request for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(
                ex,
                "[Jellycheckr] Failed to process prompt-shown for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            return StatusCode(500, new { error = "Failed to process prompt-shown request." });
        }
    }
}

