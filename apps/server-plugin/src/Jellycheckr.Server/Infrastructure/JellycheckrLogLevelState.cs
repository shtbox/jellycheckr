using Jellycheckr.Server.Models;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Infrastructure;

internal static class JellycheckrLogLevelState
{
    private static readonly object Sync = new();
    private static bool _initialized;
    private static LogLevel _minimumLogLevel = LogLevel.Warning;

    public static LogLevel GetMinimumLogLevel()
    {
        EnsureInitialized();
        return _minimumLogLevel;
    }

    public static void Apply(LogLevel minimumLogLevel)
    {
        lock (Sync)
        {
            _minimumLogLevel = minimumLogLevel;
            _initialized = true;
        }
    }

    public static void Apply(PluginConfig? config)
    {
        if (config is null)
        {
            return;
        }

        Apply(config.MinimumLogLevel);
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _minimumLogLevel = Plugin.Instance?.Configuration?.MinimumLogLevel ?? LogLevel.Warning;
            _initialized = true;
        }
    }
}
