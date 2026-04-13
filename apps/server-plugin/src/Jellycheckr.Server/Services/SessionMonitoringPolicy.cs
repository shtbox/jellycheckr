namespace Jellycheckr.Server.Services;

internal static class SessionMonitoringPolicy
{
    internal static readonly TimeSpan MissingSessionGrace = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan NoCurrentItemResetGrace = MissingSessionGrace;
}
