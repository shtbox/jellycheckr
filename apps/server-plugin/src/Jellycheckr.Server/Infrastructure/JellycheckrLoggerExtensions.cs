using System;
using Jellycheckr.Server.Infrastructure;

namespace Microsoft.Extensions.Logging;

public static class JellycheckrLoggerExtensions
{
    private const string Prefix = "[Jellycheckr] ";

    public static void LogJellycheckrTrace(this ILogger logger, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Trace, null, message, args);

    public static void LogJellycheckrDebug(this ILogger logger, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Debug, null, message, args);

    public static void LogJellycheckrDebug(this ILogger logger, Exception? exception, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Debug, exception, message, args);

    public static void LogJellycheckrInformation(this ILogger logger, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Information, null, message, args);

    public static void LogJellycheckrInformation(this ILogger logger, Exception? exception, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Information, exception, message, args);

    public static void LogJellycheckrWarning(this ILogger logger, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Warning, null, message, args);

    public static void LogJellycheckrWarning(this ILogger logger, Exception? exception, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Warning, exception, message, args);

    public static void LogJellycheckrError(this ILogger logger, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Error, null, message, args);

    public static void LogJellycheckrError(this ILogger logger, Exception? exception, string message, params object?[] args)
        => LogAndWrite(logger, LogLevel.Error, exception, message, args);

    private static void LogAndWrite(ILogger logger, LogLevel level, Exception? exception, string message, params object?[] args)
    {
        var prefixed = PrefixMessage(message);
        logger.Log(level, exception, prefixed, args);
        JellycheckrFileLogSink.TryWrite(level, prefixed, exception, args);
    }

    private static string PrefixMessage(string message)
    {
        if (message.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return message;
        }

        return Prefix + message;
    }
}
