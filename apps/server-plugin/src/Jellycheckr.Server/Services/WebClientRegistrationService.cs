using Jellycheckr.Contracts;
using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IWebClientRegistrationService
{
    WebClientRegisterResponse Register(string? userId, WebClientRegisterRequest request);
    WebClientHeartbeatResponse Heartbeat(string? userId, WebClientHeartbeatRequest request);
    void Unregister(string? userId, WebClientUnregisterRequest request);
}

public sealed class WebClientRegistrationService : IWebClientRegistrationService
{
    private readonly ISessionStateStore _sessionStateStore;
    private readonly ISessionOwnershipValidator _sessionOwnershipValidator;
    private readonly IConfigService _configService;
    private readonly IWebClientSessionResolver _sessionResolver;
    private readonly ISessionStateObserver _sessionStateObserver;
    private readonly IClock _clock;
    private readonly ILogger<WebClientRegistrationService> _logger;

    public WebClientRegistrationService(
        ISessionStateStore sessionStateStore,
        ISessionOwnershipValidator sessionOwnershipValidator,
        IConfigService configService,
        IWebClientSessionResolver sessionResolver,
        ISessionStateObserver sessionStateObserver,
        IClock clock,
        ILogger<WebClientRegistrationService> logger)
    {
        _sessionStateStore = sessionStateStore;
        _sessionOwnershipValidator = sessionOwnershipValidator;
        _configService = configService;
        _sessionResolver = sessionResolver;
        _sessionStateObserver = sessionStateObserver;
        _clock = clock;
        _logger = logger;
    }

    public WebClientRegisterResponse Register(string? userId, WebClientRegisterRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new ArgumentException("Device id is required.", nameof(request.DeviceId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Web client register rejected due to missing authenticated user id.");
            return new WebClientRegisterResponse
            {
                Registered = false,
                Reason = "session_unresolved"
            };
        }

        var snapshot = _sessionResolver.Resolve(userId, request.DeviceId);
        if (snapshot is null)
        {
            _logger.LogJellycheckrTrace(
                "Web client register unresolved deviceId={DeviceId} userId={UserId}",
                JellycheckrLogSanitizer.RedactIdentifier(request.DeviceId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            return new WebClientRegisterResponse
            {
                Registered = false,
                Reason = "session_unresolved"
            };
        }

        if (!_sessionOwnershipValidator.CanMutateSession(userId, snapshot.SessionId))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Web client register rejected by session ownership policy session={SessionId} userId={UserId}.",
                JellycheckrLogSanitizer.RedactIdentifier(snapshot.SessionId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            return new WebClientRegisterResponse
            {
                Registered = false,
                Reason = "session_unresolved"
            };
        }

        var now = _clock.UtcNow;
        var state = _sessionStateStore.GetOrCreate(snapshot.SessionId);
        _sessionStateObserver.ObserveSession(state, snapshot, now);
        WebUiRegistrationLeasePolicy.ApplyRegistration(state, now);
        state.LastFallbackAction = "web_ui_registration";
        state.LastFallbackActionResult = "registered";

        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Registered web client session={SessionId} deviceId={DeviceId} leaseUntilUtc={LeaseUntilUtc}.",
            JellycheckrLogSanitizer.RedactIdentifier(state.SessionId),
            JellycheckrLogSanitizer.RedactIdentifier(request.DeviceId),
            state.WebUiRegistrationLeaseUtc);

        return new WebClientRegisterResponse
        {
            Registered = true,
            SessionId = state.SessionId,
            LeaseExpiresUtc = state.WebUiRegistrationLeaseUtc,
            Config = _configService.GetEffectiveConfig(userId)
        };
    }

    public WebClientHeartbeatResponse Heartbeat(string? userId, WebClientHeartbeatRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new ArgumentException("Device id is required.", nameof(request.DeviceId));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogJellycheckrWarning("[Jellycheckr] Web client heartbeat rejected due to missing authenticated user id.");
            return new WebClientHeartbeatResponse
            {
                Accepted = false,
                Reason = "session_unresolved"
            };
        }

        var snapshot = _sessionResolver.Resolve(userId, request.DeviceId);
        if (snapshot is null)
        {
            _logger.LogJellycheckrTrace(
                "Web client heartbeat unresolved deviceId={DeviceId} userId={UserId}",
                JellycheckrLogSanitizer.RedactIdentifier(request.DeviceId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            return new WebClientHeartbeatResponse
            {
                Accepted = false,
                Reason = "session_unresolved"
            };
        }

        if (!_sessionOwnershipValidator.CanMutateSession(userId, snapshot.SessionId))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Web client heartbeat rejected by session ownership policy session={SessionId} userId={UserId}.",
                JellycheckrLogSanitizer.RedactIdentifier(snapshot.SessionId),
                JellycheckrLogSanitizer.RedactIdentifier(userId));
            return new WebClientHeartbeatResponse
            {
                Accepted = false,
                Reason = "session_unresolved"
            };
        }

        var now = _clock.UtcNow;
        var state = _sessionStateStore.GetOrCreate(snapshot.SessionId);
        _sessionStateObserver.ObserveSession(state, snapshot, now);
        WebUiRegistrationLeasePolicy.ApplyRegistration(state, now);
        state.LastFallbackAction = "web_ui_registration";
        state.LastFallbackActionResult = "heartbeat";

        _logger.LogJellycheckrTrace(
            "Web client heartbeat accepted session={SessionId} deviceId={DeviceId} requestedSessionId={RequestedSessionId} leaseUntilUtc={LeaseUntilUtc}",
            JellycheckrLogSanitizer.RedactIdentifier(state.SessionId),
            JellycheckrLogSanitizer.RedactIdentifier(request.DeviceId),
            JellycheckrLogSanitizer.RedactIdentifier(request.SessionId),
            state.WebUiRegistrationLeaseUtc);

        return new WebClientHeartbeatResponse
        {
            Accepted = true,
            SessionId = state.SessionId,
            LeaseExpiresUtc = state.WebUiRegistrationLeaseUtc
        };
    }

    public void Unregister(string? userId, WebClientUnregisterRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SessionId) && string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new ArgumentException("Either session id or device id is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("Authenticated user id is required for unregister.");
        }

        var state = ResolveStateForUnregister(userId, request);
        if (state is null)
        {
            _logger.LogJellycheckrTrace(
                "Web client unregister found no state userId={UserId} sessionId={SessionId} deviceId={DeviceId}",
                JellycheckrLogSanitizer.RedactIdentifier(userId),
                JellycheckrLogSanitizer.RedactIdentifier(request.SessionId),
                JellycheckrLogSanitizer.RedactIdentifier(request.DeviceId));
            return;
        }

        WebUiRegistrationLeasePolicy.ClearRegistration(state);
        state.LastFallbackAction = "web_ui_registration";
        state.LastFallbackActionResult = "unregistered";

        _logger.LogJellycheckrInformation(
            "[Jellycheckr] Unregistered web client session={SessionId}.",
            JellycheckrLogSanitizer.RedactIdentifier(state.SessionId));
    }

    private SessionState? ResolveStateForUnregister(string? userId, WebClientUnregisterRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            if (!_sessionOwnershipValidator.CanMutateSession(userId, request.SessionId))
            {
                throw new UnauthorizedAccessException("Session is not owned by the current user.");
            }

            return FindStateBySessionId(request.SessionId);
        }

        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            return null;
        }

        var snapshot = _sessionResolver.Resolve(userId, request.DeviceId);
        if (snapshot is null)
        {
            return null;
        }

        if (!_sessionOwnershipValidator.CanMutateSession(userId, snapshot.SessionId))
        {
            throw new UnauthorizedAccessException("Session is not owned by the current user.");
        }

        return FindStateBySessionId(snapshot.SessionId);
    }

    private SessionState? FindStateBySessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return _sessionStateStore.Snapshot()
            .FirstOrDefault(state => string.Equals(state.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
    }
}
