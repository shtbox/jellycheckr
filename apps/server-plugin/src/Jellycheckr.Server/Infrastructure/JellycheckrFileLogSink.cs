using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Jellycheckr.Server.Infrastructure;

internal static class JellycheckrFileLogSink
{
    private static readonly object WriteSync = new();
    private static DateOnly _activeDate;
    private static string? _activeLogPath;
    private static bool _initialized;

    public static void TryWrite(LogLevel level, string messageTemplate, Exception? exception, params object?[] args)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var logPath = ResolveLogPath(now);
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return;
            }

            var renderedMessage = RenderMessage(messageTemplate, args);
            var builder = new StringBuilder(256);
            builder.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append('[').Append(GetLevelToken(level)).Append("] ");
            builder.Append(renderedMessage);
            builder.AppendLine();
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (WriteSync)
            {
                EnsureInitialized(logPath);
                File.AppendAllText(logPath, builder.ToString());
            }
        }
        catch
        {
            // Intentionally swallow file sink failures to avoid impacting plugin behavior.
        }
    }

    private static bool IsEnabled(LogLevel level)
    {
        if (level == LogLevel.None)
        {
            return false;
        }

        var configuredLevel = JellycheckrLogLevelState.GetMinimumLogLevel();
        return level >= configuredLevel;
    }

    private static string? ResolveLogPath(DateTimeOffset now)
    {
        var utcDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (_activeLogPath is not null && _activeDate == utcDate)
        {
            return _activeLogPath;
        }

        var logDirectory = ResolveLogDirectory();
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch
        {
            return null;
        }

        _activeDate = utcDate;
        _activeLogPath = Path.Combine(logDirectory, $"jellycheckr_{now:yyyyMMdd}.log");
        _initialized = false;
        return _activeLogPath;
    }

    private static string? ResolveLogDirectory()
    {
        foreach (var candidate in EnumerateLogDirectoryCandidates())
        {
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateLogDirectoryCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applicationPaths = Plugin.ApplicationPaths;
        var configuredLogDirectory = applicationPaths?.LogDirectoryPath;
        if (!string.IsNullOrWhiteSpace(configuredLogDirectory) && seen.Add(configuredLogDirectory))
        {
            yield return configuredLogDirectory;
        }

        var programDataPath = applicationPaths?.ProgramDataPath;
        if (!string.IsNullOrWhiteSpace(programDataPath))
        {
            var logPath = Path.Combine(programDataPath, "log");
            if (seen.Add(logPath))
            {
                yield return logPath;
            }

            var logsPath = Path.Combine(programDataPath, "logs");
            if (seen.Add(logsPath))
            {
                yield return logsPath;
            }
        }

        var baseLogsPath = Path.Combine(AppContext.BaseDirectory, "logs");
        if (seen.Add(baseLogsPath))
        {
            yield return baseLogsPath;
        }
    }

    private static void EnsureInitialized(string logPath)
    {
        if (_initialized && File.Exists(logPath))
        {
            return;
        }

        using var _ = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        _initialized = true;
    }

    private static string RenderMessage(string template, object?[] args)
    {
        if (string.IsNullOrEmpty(template) || args.Length == 0)
        {
            return template;
        }

        var builder = new StringBuilder(template.Length + (args.Length * 8));
        var argIndex = 0;
        var index = 0;

        while (index < template.Length)
        {
            if (template[index] == '{')
            {
                if (index + 1 < template.Length && template[index + 1] == '{')
                {
                    builder.Append('{');
                    index += 2;
                    continue;
                }

                var closeIndex = template.IndexOf('}', index + 1);
                if (closeIndex > index)
                {
                    if (argIndex < args.Length)
                    {
                        builder.Append(FormatArg(args[argIndex]));
                        argIndex++;
                    }
                    else
                    {
                        builder.Append(template, index, closeIndex - index + 1);
                    }

                    index = closeIndex + 1;
                    continue;
                }
            }

            if (template[index] == '}' && index + 1 < template.Length && template[index + 1] == '}')
            {
                builder.Append('}');
                index += 2;
                continue;
            }

            builder.Append(template[index]);
            index++;
        }

        if (argIndex < args.Length)
        {
            builder.Append(" | extraArgs=[");
            for (var i = argIndex; i < args.Length; i++)
            {
                if (i > argIndex)
                {
                    builder.Append(", ");
                }

                builder.Append(FormatArg(args[i]));
            }

            builder.Append(']');
        }

        return builder.ToString();
    }

    private static string FormatArg(object? value)
    {
        return value switch
        {
            null => "(null)",
            DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string GetLevelToken(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "UNK"
        };
    }
}
