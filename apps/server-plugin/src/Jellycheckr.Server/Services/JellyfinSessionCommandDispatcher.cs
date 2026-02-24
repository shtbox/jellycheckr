using System.Reflection;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IJellyfinSessionCommandDispatcher
{
    Task<bool> TrySendPauseAsync(string sessionId, string? controllingUserId, CancellationToken cancellationToken);
    Task<bool> TrySendStopAsync(string sessionId, string? controllingUserId, CancellationToken cancellationToken);
    Task<bool> TrySendMessageAsync(string sessionId, string? controllingUserId, string message, CancellationToken cancellationToken);
}

public sealed class JellyfinSessionCommandDispatcher : IJellyfinSessionCommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JellyfinSessionCommandDispatcher> _logger;

    public JellyfinSessionCommandDispatcher(
        IServiceProvider serviceProvider,
        ILogger<JellyfinSessionCommandDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task<bool> TrySendPauseAsync(string sessionId, string? controllingUserId, CancellationToken cancellationToken)
        => TrySendPlaystateAsync(sessionId, controllingUserId, PlaystateCommand.Pause, cancellationToken);

    public Task<bool> TrySendStopAsync(string sessionId, string? controllingUserId, CancellationToken cancellationToken)
        => TrySendPlaystateAsync(sessionId, controllingUserId, PlaystateCommand.Stop, cancellationToken);

    public async Task<bool> TrySendMessageAsync(string sessionId, string? controllingUserId, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var sessionManager = _serviceProvider.GetService<ISessionManager>();
        if (sessionManager is null)
        {
            return false;
        }

        try
        {
            var command = (MessageCommand?)Activator.CreateInstance(typeof(MessageCommand));
            if (command is null)
            {
                return false;
            }

            SetPropertyIfPresent(command, "Header", "Jellycheckr");
            SetPropertyIfPresent(command, "Title", "Jellycheckr");
            SetPropertyIfPresent(command, "Text", message);
            SetPropertyIfPresent(command, "Message", message);
            SetPropertyIfPresent(command, "TimeoutMs", 10000);
            SetPropertyIfPresent(command, "Timeout", 10);

            await sessionManager.SendMessageCommand(
                sessionId,
                sessionId,
                command,
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Jellycheckr] Failed to send message command to session={SessionId}.", sessionId);
            return false;
        }
    }

    private async Task<bool> TrySendPlaystateAsync(
        string sessionId,
        string? controllingUserId,
        PlaystateCommand command,
        CancellationToken cancellationToken)
    {
        var sessionManager = _serviceProvider.GetService<ISessionManager>();
        if (sessionManager is null)
        {
            return false;
        }

        try
        {
            var request = new PlaystateRequest();
            SetPropertyIfPresent(request, "ControllingUserId", controllingUserId ?? string.Empty);
            SetPropertyIfPresent(request, "Command", command);

            await sessionManager.SendPlaystateCommand(
                sessionId,
                sessionId,
                request,
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Jellycheckr] Failed to send playstate command {Command} to session={SessionId}.", command, sessionId);
            return false;
        }
    }

    private static void SetPropertyIfPresent(object target, string propertyName, object? value)
    {
        if (value is null)
        {
            return;
        }

        var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null || !prop.CanWrite)
        {
            return;
        }

        try
        {
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object? finalValue = value;
            if (!targetType.IsInstanceOfType(value))
            {
                finalValue = targetType.IsEnum && value is Enum
                    ? Enum.Parse(targetType, value.ToString()!, ignoreCase: true)
                    : Convert.ChangeType(value, targetType);
            }

            prop.SetValue(target, finalValue);
        }
        catch
        {
            // Best effort: ignore unsupported properties across Jellyfin versions.
        }
    }
}
