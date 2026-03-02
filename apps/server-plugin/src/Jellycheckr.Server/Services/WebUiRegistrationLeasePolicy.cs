using Jellycheckr.Server.Models;

namespace Jellycheckr.Server.Services;

public static class WebUiRegistrationLeasePolicy
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    public static void ApplyRegistration(SessionState state, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.WebUiRegistered = true;
        state.WebUiRegistrationLeaseUtc = nowUtc.Add(LeaseDuration);
    }

    public static void ClearRegistration(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.WebUiRegistered = false;
        state.WebUiRegistrationLeaseUtc = null;
    }

    public static bool HasActiveRegistration(SessionState state, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.WebUiRegistered
               && state.WebUiRegistrationLeaseUtc.HasValue
               && nowUtc <= state.WebUiRegistrationLeaseUtc.Value;
    }
}
