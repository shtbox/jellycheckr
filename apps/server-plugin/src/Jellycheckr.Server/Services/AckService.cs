using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IAckService
{
    AckResponse HandleAck(string sessionId, string? userId, AckRequest request, EffectiveConfigResponse config);
    InteractionResponse HandleInteraction(string sessionId, string? userId, InteractionRequest request);
    void MarkPromptActive(string sessionId, string? userId, DateTimeOffset promptDeadlineUtc, string? clientType);
}

public sealed class AckService : IAckService
{
    private readonly ISessionStateStore _sessionStateStore;
    private readonly ISessionOwnershipValidator _sessionOwnershipValidator;
    private readonly IClock _clock;
    private readonly ILogger<AckService> _logger;

    public AckService(
        ISessionStateStore sessionStateStore,
        ISessionOwnershipValidator sessionOwnershipValidator,
        IClock clock,
        ILogger<AckService> logger)
    {
        _sessionStateStore = sessionStateStore;
        _sessionOwnershipValidator = sessionOwnershipValidator;
        _clock = clock;
        _logger = logger;
    }

    public AckResponse HandleAck(string sessionId, string? userId, AckRequest request, EffectiveConfigResponse config)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting ack due to missing session id.");
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (request is null)
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting ack due to missing request body for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.AckType))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting ack due to missing ack type for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw new ArgumentException("Ack type is required.", nameof(request.AckType));
        }

        if (!_sessionOwnershipValidator.CanMutateSession(userId, sessionId))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting ack due to ownership mismatch session={SessionId} userId={UserId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            throw new UnauthorizedAccessException("Session is not owned by the current user.");
        }

        try
        {
            var now = _clock.UtcNow;
            var state = _sessionStateStore.GetOrCreate(sessionId);
            var resetApplied = request.AckType.Equals("continue", StringComparison.OrdinalIgnoreCase)
                || request.AckType.Equals("stop", StringComparison.OrdinalIgnoreCase);

            if (resetApplied)
            {
                state.LastAckUtc = now;
                state.PromptActive = false;
                state.PromptDeadlineUtc = null;
                state.ConsecutiveEpisodesSinceAck = 0;
                state.ServerFallbackEpisodeTransitionsSinceReset = 0;
                state.ServerFallbackPlaybackTicksSinceReset = 0;
                state.FallbackPhase = ServerFallbackPhase.Monitoring;
                state.PauseIssuedUtc = null;
                state.PauseGraceDeadlineUtc = null;
                state.LastFallbackAction = "ack";
                state.LastFallbackActionResult = request.AckType;
                state.LastInferredActivityUtc = now;
                state.NextEligiblePromptUtc = now.AddMinutes(config.CooldownMinutes);
            }

            state.LastItemId = request.ItemId;
            RefreshWebUiRegistrationLeaseIfApplicable(state, request.ClientType, now);

            _logger.LogJellycheckrInformation(
                "[Jellycheckr] AYSW ack: session={SessionId} ackType={AckType} reason={Reason} reset={ResetApplied}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                request.AckType,
                string.IsNullOrWhiteSpace(request.Reason) ? "(none)" : "provided",
                resetApplied);

            _logger.LogJellycheckrTrace(
                "Ack processed session={SessionId} ackType={AckType} reset={ResetApplied} nextEligiblePromptUtc={NextEligiblePromptUtc}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                request.AckType,
                resetApplied,
                state.NextEligiblePromptUtc);

            return new AckResponse
            {
                ResetApplied = resetApplied,
                NextEligiblePromptUtc = state.NextEligiblePromptUtc
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(
                ex,
                "[Jellycheckr] Ack request validation failed for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Ack request rejected by session ownership policy.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(
                ex,
                "[Jellycheckr] Unhandled error while processing ack for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw;
        }
    }

    public InteractionResponse HandleInteraction(string sessionId, string? userId, InteractionRequest request)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting interaction due to missing session id.");
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (request is null)
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting interaction due to missing request body for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting interaction due to missing event type for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw new ArgumentException("Event type is required.", nameof(request.EventType));
        }

        if (!_sessionOwnershipValidator.CanMutateSession(userId, sessionId))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting interaction due to ownership mismatch session={SessionId} userId={UserId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            throw new UnauthorizedAccessException("Session is not owned by the current user.");
        }

        try
        {
            var now = _clock.UtcNow;
            var state = _sessionStateStore.GetOrCreate(sessionId);
            state.LastInteractionUtc = now;
            state.LastItemId = request.ItemId ?? state.LastItemId;
            RefreshWebUiRegistrationLeaseIfApplicable(state, request.ClientType, now);

            _logger.LogJellycheckrTrace(
                "Interaction processed session={SessionId} eventType={EventType} nowUtc={NowUtc}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                request.EventType,
                now);

            return new InteractionResponse
            {
                Accepted = true,
                ReceivedAtUtc = now
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(
                ex,
                "[Jellycheckr] Interaction request validation failed for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Interaction request rejected by session ownership policy.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(
                ex,
                "[Jellycheckr] Unhandled error while processing interaction for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw;
        }
    }

    public void MarkPromptActive(string sessionId, string? userId, DateTimeOffset promptDeadlineUtc, string? clientType)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting prompt-shown due to missing session id.");
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (promptDeadlineUtc <= DateTimeOffset.MinValue)
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting prompt-shown due to invalid deadline for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw new ArgumentOutOfRangeException(nameof(promptDeadlineUtc), "Prompt deadline must be a valid UTC timestamp.");
        }

        if (!_sessionOwnershipValidator.CanMutateSession(userId, sessionId))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Rejecting prompt-shown due to ownership mismatch session={SessionId} userId={UserId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            throw new UnauthorizedAccessException("Session is not owned by the current user.");
        }

        try
        {
            var now = _clock.UtcNow;
            var state = _sessionStateStore.GetOrCreate(sessionId);
            state.PromptActive = true;
            state.PromptDeadlineUtc = promptDeadlineUtc;
            RefreshWebUiRegistrationLeaseIfApplicable(state, clientType, now);
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Prompt marked active for session={SessionId} deadlineUtc={PromptDeadlineUtc}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                promptDeadlineUtc);
            _logger.LogJellycheckrTrace(
                "Prompt active state updated session={SessionId} deadlineUtc={PromptDeadlineUtc} clientType={ClientType}",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId),
                promptDeadlineUtc,
                clientType ?? "(none)");
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(
                ex,
                "[Jellycheckr] Prompt-shown validation failed for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Prompt-shown request rejected by session ownership policy.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(
                ex,
                "[Jellycheckr] Unhandled error while marking prompt active for session={SessionId}.",
                JellycheckrLogSanitizer.RedactIdentifier(sessionId));
            throw;
        }
    }

    private static void RefreshWebUiRegistrationLeaseIfApplicable(SessionState state, string? clientType, DateTimeOffset nowUtc)
    {
        if (!string.Equals(clientType, "web", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        WebUiRegistrationLeasePolicy.ApplyRegistration(state, nowUtc);
    }
}

