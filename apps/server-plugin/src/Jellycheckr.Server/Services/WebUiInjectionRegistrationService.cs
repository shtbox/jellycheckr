using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Jellycheckr.Server.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public sealed class WebUiInjectionRegistrationService : IHostedService
{
    private static readonly Guid TransformationId = Guid.Parse("8b8d73b4-8a11-4a3b-9f15-57fe8f0e7f4b");

    private readonly ILogger<WebUiInjectionRegistrationService> _logger;

    public WebUiInjectionRegistrationService(ILogger<WebUiInjectionRegistrationService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!EmbeddedWebClientBundle.TryGetBundle(out _))
        {
            _logger.LogWarning("[Jellycheckr] Embedded web bundle was not found; web UI injection may succeed but serve 404.");
        }

        try
        {
            if (TryRegisterFileTransformation())
            {
                _logger.LogInformation("[Jellycheckr] Registered Jellyfin Web index.html transformation for web client injection.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Jellycheckr] Failed to register File Transformation callback for web UI injection.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool TryRegisterFileTransformation()
    {
        var pluginInterfaceType = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .Select(assembly => assembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface", throwOnError: false))
            .FirstOrDefault(type => type is not null);

        if (pluginInterfaceType is null)
        {
            _logger.LogInformation("[Jellycheckr] File Transformation plugin not detected; skipping web UI injection registration.");
            return false;
        }

        var registerMethod = pluginInterfaceType.GetMethod(
            "RegisterTransformation",
            BindingFlags.Public | BindingFlags.Static);

        if (registerMethod is null)
        {
            _logger.LogWarning("[Jellycheckr] File Transformation plugin API was found but RegisterTransformation was missing.");
            return false;
        }

        var payloadParameterType = registerMethod.GetParameters().SingleOrDefault()?.ParameterType;
        if (payloadParameterType is null)
        {
            _logger.LogWarning("[Jellycheckr] File Transformation RegisterTransformation signature was unexpected.");
            return false;
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
            _logger.LogWarning("[Jellycheckr] Could not create File Transformation payload; JObject.Parse was unavailable.");
            return false;
        }

        var payload = parseMethod.Invoke(null, new object?[] { payloadJson });
        registerMethod.Invoke(null, new[] { payload });
        return true;
    }
}
