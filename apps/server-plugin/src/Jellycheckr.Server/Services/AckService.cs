using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IAckService
{
    AckResponse HandleAck(string sessionId, AckRequest request, EffectiveConfigResponse config);
    InteractionResponse HandleInteraction(string sessionId, InteractionRequest request);
    void MarkPromptActive(string sessionId, DateTimeOffset promptDeadlineUtc);
}

public sealed class AckService : IAckService
{
    private readonly ISessionStateStore _sessionStateStore;
    private readonly IClock _clock;
    private readonly ILogger<AckService> _logger;

    public AckService(
        ISessionStateStore sessionStateStore,
        IClock clock,
        ILogger<AckService> logger)
    {
        _sessionStateStore = sessionStateStore;
        _clock = clock;
        _logger = logger;
    }

    public AckResponse HandleAck(string sessionId, AckRequest request, EffectiveConfigResponse config)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting ack due to missing session id.");
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (request is null)
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting ack due to missing request body for session={SessionId}.", sessionId);
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.AckType))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting ack due to missing ack type for session={SessionId}.", sessionId);
            throw new ArgumentException("Ack type is required.", nameof(request.AckType));
        }

        try
        {
            var now = _clock.UtcNow;
            var state = _sessionStateStore.GetOrCreate(sessionId);
            var stateBefore = SnapshotState(state);
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

            _logger.LogJellycheckrInformation(
                "[Jellycheckr] AYSW ack: session={SessionId} ackType={AckType} reason={Reason} reset={ResetApplied}",
                sessionId,
                request.AckType,
                request.Reason,
                resetApplied);

            _logger.LogJellycheckrTrace(
                "Ack processed session={SessionId} request={@Request} nowUtc={NowUtc} stateBefore={@StateBefore} stateAfter={@StateAfter}",
                sessionId,
                request,
                now,
                stateBefore,
                SnapshotState(state));

            return new AckResponse
            {
                ResetApplied = resetApplied,
                NextEligiblePromptUtc = state.NextEligiblePromptUtc
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Ack request validation failed for session={SessionId}.", sessionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Unhandled error while processing ack for session={SessionId}.", sessionId);
            throw;
        }
    }

    public InteractionResponse HandleInteraction(string sessionId, InteractionRequest request)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting interaction due to missing session id.");
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (request is null)
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting interaction due to missing request body for session={SessionId}.", sessionId);
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting interaction due to missing event type for session={SessionId}.", sessionId);
            throw new ArgumentException("Event type is required.", nameof(request.EventType));
        }

        try
        {
            var now = _clock.UtcNow;
            var state = _sessionStateStore.GetOrCreate(sessionId);
            var stateBefore = SnapshotState(state);
            state.LastInteractionUtc = now;
            state.LastItemId = request.ItemId ?? state.LastItemId;

            _logger.LogJellycheckrTrace(
                "Interaction processed session={SessionId} request={@Request} nowUtc={NowUtc} stateBefore={@StateBefore} stateAfter={@StateAfter}",
                sessionId,
                request,
                now,
                stateBefore,
                SnapshotState(state));

            return new InteractionResponse
            {
                Accepted = true,
                ReceivedAtUtc = now
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Interaction request validation failed for session={SessionId}.", sessionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Unhandled error while processing interaction for session={SessionId}.", sessionId);
            throw;
        }
    }

    public void MarkPromptActive(string sessionId, DateTimeOffset promptDeadlineUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting prompt-shown due to missing session id.");
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (promptDeadlineUtc <= DateTimeOffset.MinValue)
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Rejecting prompt-shown due to invalid deadline for session={SessionId}.", sessionId);
            throw new ArgumentOutOfRangeException(nameof(promptDeadlineUtc), "Prompt deadline must be a valid UTC timestamp.");
        }

        try
        {
            var state = _sessionStateStore.GetOrCreate(sessionId);
            var stateBefore = SnapshotState(state);
            state.PromptActive = true;
            state.PromptDeadlineUtc = promptDeadlineUtc;
            _logger.LogJellycheckrInformation(
                "[Jellycheckr] Prompt marked active for session={SessionId} deadlineUtc={PromptDeadlineUtc}.",
                sessionId,
                promptDeadlineUtc);
            _logger.LogJellycheckrTrace(
                "Prompt active state updated session={SessionId} stateBefore={@StateBefore} stateAfter={@StateAfter}",
                sessionId,
                stateBefore,
                SnapshotState(state));
        }
        catch (ArgumentException ex)
        {
            _logger.LogJellycheckrWarning(ex, "[Jellycheckr] Prompt-shown validation failed for session={SessionId}.", sessionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrError(ex, "[Jellycheckr] Unhandled error while marking prompt active for session={SessionId}.", sessionId);
            throw;
        }
    }

    private static object SnapshotState(SessionState state)
    {
        return new
        {
            state.SessionId,
            state.LastAckUtc,
            state.LastInteractionUtc,
            state.PromptActive,
            state.PromptDeadlineUtc,
            state.LastItemId,
            state.ConsecutiveEpisodesSinceAck,
            state.NextEligiblePromptUtc,
            state.UserId,
            state.UserName,
            state.ClientName,
            state.DeviceName,
            state.DeviceId,
            state.CurrentItemId,
            state.CurrentItemName,
            state.PreviousItemId,
            state.LastSeenUtc,
            state.LastObservedPositionTicks,
            state.LastPlaybackProgressObservedUtc,
            state.LastObservedLastActivityDateUtc,
            state.LastObservedLastPlaybackCheckInUtc,
            state.LastObservedLastPausedDateUtc,
            state.LastInferredActivityUtc,
            state.ServerFallbackEpisodeTransitionsSinceReset,
            state.ServerFallbackPlaybackTicksSinceReset,
            state.IsPaused,
            state.FallbackPhase,
            state.PauseIssuedUtc,
            state.PauseGraceDeadlineUtc,
            state.LastFallbackAction,
            state.LastFallbackActionResult,
            state.LastFallbackDecisionKey,
            state.LastFallbackDecisionLoggedUtc
        };
    }
}

