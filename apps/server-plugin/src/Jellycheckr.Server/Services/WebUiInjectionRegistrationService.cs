using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Jellycheckr.Server.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public sealed class WebUiInjectionRegistrationService : BackgroundService
{
    private static readonly Guid TransformationId = Guid.Parse("8b8d73b4-8a11-4a3b-9f15-57fe8f0e7f4b");
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    private readonly ILogger<WebUiInjectionRegistrationService> _logger;
    private readonly IWebUiInjectionState _injectionState;

    public WebUiInjectionRegistrationService(
        ILogger<WebUiInjectionRegistrationService> logger,
        IWebUiInjectionState injectionState)
    {
        _logger = logger;
        _injectionState = injectionState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _injectionState.SetRegistered(false);

        if (!PluginWebAssetRegistry.Exists(PluginWebAssetRegistry.WebClientBundleKey, out var webBundlePath))
        {
            _logger.LogJellycheckrWarning(
                "[Jellycheckr] Web bundle asset was not found at {WebBundlePath}; web UI injection may succeed but serve 404.",
                webBundlePath);
        }

        string? lastFailureCode = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var attempt = TryRegisterFileTransformation();
            if (attempt.Registered)
            {
                _injectionState.SetRegistered(true);
                _logger.LogJellycheckrInformation("[Jellycheckr] Registered Jellyfin Web index.html transformation for web client injection.");
                return;
            }

            _injectionState.SetRegistered(false);
            if (!string.Equals(lastFailureCode, attempt.FailureCode, StringComparison.Ordinal))
            {
                LogAttemptFailure(attempt);
                lastFailureCode = attempt.FailureCode;
            }

            await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    private RegistrationAttemptResult TryRegisterFileTransformation()
    {
        try
        {
            var pluginInterfaceType = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .Select(assembly => assembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface", throwOnError: false))
                .FirstOrDefault(type => type is not null);

            if (pluginInterfaceType is null)
            {
                return RegistrationAttemptResult.Failed("plugin_not_detected");
            }

            var registerMethod = pluginInterfaceType.GetMethod(
                "RegisterTransformation",
                BindingFlags.Public | BindingFlags.Static);

            if (registerMethod is null)
            {
                return RegistrationAttemptResult.Failed("register_method_missing");
            }

            var payloadParameterType = registerMethod.GetParameters().SingleOrDefault()?.ParameterType;
            if (payloadParameterType is null)
            {
                return RegistrationAttemptResult.Failed("payload_signature_unexpected");
            }

            var payloadJson = JsonSerializer.Serialize(new
            {
                id = TransformationId,
                fileNamePattern = "index\\.html$",
                callbackAssembly = typeof(JellycheckrWebIndexTransform).Assembly.FullName,
                callbackClass = typeof(JellycheckrWebIndexTransform).FullName,
                callbackMethod = nameof(JellycheckrWebIndexTransform.Transform)
            });

            var parseMethod = payloadParameterType.GetMethod(
                "Parse",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            if (parseMethod is null)
            {
                return RegistrationAttemptResult.Failed("payload_parse_missing");
            }

            var payload = parseMethod.Invoke(null, new object?[] { payloadJson });
            registerMethod.Invoke(null, new[] { payload });
            return RegistrationAttemptResult.Success();
        }
        catch (Exception ex)
        {
            return RegistrationAttemptResult.Failed("registration_exception", ex);
        }
    }

    private void LogAttemptFailure(RegistrationAttemptResult attempt)
    {
        switch (attempt.FailureCode)
        {
            case "plugin_not_detected":
                _logger.LogJellycheckrInformation("[Jellycheckr] File Transformation plugin not detected; web UI injection registration will retry.");
                break;
            case "register_method_missing":
                _logger.LogJellycheckrWarning("[Jellycheckr] File Transformation plugin API was found but RegisterTransformation was missing; retrying.");
                break;
            case "payload_signature_unexpected":
                _logger.LogJellycheckrWarning("[Jellycheckr] File Transformation RegisterTransformation signature was unexpected; retrying.");
                break;
            case "payload_parse_missing":
                _logger.LogJellycheckrWarning("[Jellycheckr] Could not create File Transformation payload because Parse(string) was unavailable; retrying.");
                break;
            default:
                _logger.LogJellycheckrWarning(
                    attempt.Exception,
                    "[Jellycheckr] Failed to register File Transformation callback for web UI injection; retrying.");
                break;
        }
    }

    private readonly record struct RegistrationAttemptResult(bool Registered, string? FailureCode, Exception? Exception)
    {
        public static RegistrationAttemptResult Success() => new(true, null, null);

        public static RegistrationAttemptResult Failed(string failureCode, Exception? exception = null)
            => new(false, failureCode, exception);
    }
}

