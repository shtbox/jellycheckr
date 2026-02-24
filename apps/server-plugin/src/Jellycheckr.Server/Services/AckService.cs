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
    private readonly IConfigService _configService;
    private readonly IClock _clock;
    private readonly ILogger<AckService> _logger;

    public AckService(
        ISessionStateStore sessionStateStore,
        IConfigService configService,
        IClock clock,
        ILogger<AckService> logger)
    {
        _sessionStateStore = sessionStateStore;
        _configService = configService;
        _clock = clock;
        _logger = logger;
    }

    public AckResponse HandleAck(string sessionId, AckRequest request, EffectiveConfigResponse config)
    {
        var now = _clock.UtcNow;
        var state = _sessionStateStore.GetOrCreate(sessionId);
        var stateBefore = JellycheckrDiagnosticLogging.Describe(state);
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

        _logger.LogInformation(
            "[Jellycheckr] AYSW ack: session={SessionId} ackType={AckType} reason={Reason} reset={ResetApplied}",
            sessionId,
            request.AckType,
            request.Reason,
            resetApplied);

        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            config,
            "Ack processed session={SessionId} request={@Request} nowUtc={NowUtc} stateBefore={@StateBefore} stateAfter={@StateAfter}",
            sessionId,
            request,
            now,
            stateBefore,
            JellycheckrDiagnosticLogging.Describe(state));

        return new AckResponse
        {
            ResetApplied = resetApplied,
            NextEligiblePromptUtc = state.NextEligiblePromptUtc
        };
    }

    public InteractionResponse HandleInteraction(string sessionId, InteractionRequest request)
    {
        var now = _clock.UtcNow;
        var state = _sessionStateStore.GetOrCreate(sessionId);
        var stateBefore = JellycheckrDiagnosticLogging.Describe(state);
        state.LastInteractionUtc = now;
        state.LastItemId = request.ItemId ?? state.LastItemId;

        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "Interaction processed session={SessionId} request={@Request} nowUtc={NowUtc} stateBefore={@StateBefore} stateAfter={@StateAfter}",
            sessionId,
            request,
            now,
            stateBefore,
            JellycheckrDiagnosticLogging.Describe(state));

        return new InteractionResponse
        {
            Accepted = true,
            ReceivedAtUtc = now
        };
    }

    public void MarkPromptActive(string sessionId, DateTimeOffset promptDeadlineUtc)
    {
        var state = _sessionStateStore.GetOrCreate(sessionId);
        var stateBefore = JellycheckrDiagnosticLogging.Describe(state);
        state.PromptActive = true;
        state.PromptDeadlineUtc = promptDeadlineUtc;
        _logger.LogInformation(
            "[Jellycheckr] Prompt marked active for session={SessionId} deadlineUtc={PromptDeadlineUtc}.",
            sessionId,
            promptDeadlineUtc);
        JellycheckrDiagnosticLogging.Verbose(
            _logger,
            _configService,
            "Prompt active state updated session={SessionId} stateBefore={@StateBefore} stateAfter={@StateAfter}",
            sessionId,
            stateBefore,
            JellycheckrDiagnosticLogging.Describe(state));
    }
}
