using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    private const string PluginCategoryPrefix = "Jellycheckr.Server";

    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddLogging(builder =>
            builder.AddFilter(PluginCategoryFilter));

        serviceCollection.AddSingleton<IClock, SystemClock>();
        serviceCollection.AddSingleton<IConfigService, ConfigService>();
        serviceCollection.AddSingleton<ISessionStateStore, SessionStateStore>();
        serviceCollection.AddSingleton<IAckService, AckService>();
        serviceCollection.AddSingleton<IServerFallbackDecisionEngine, ServerFallbackDecisionEngine>();
        serviceCollection.AddSingleton<IServerFallbackSessionSnapshotProvider, ServerFallbackSessionSnapshotProvider>();
        serviceCollection.AddSingleton<IJellyfinSessionCommandDispatcher, JellyfinSessionCommandDispatcher>();
        serviceCollection.AddSingleton<IWebUiInjectionState, WebUiInjectionState>();
        serviceCollection.AddHostedService<WebUiInjectionRegistrationService>();
        serviceCollection.AddHostedService<ServerFallbackEnforcementService>();
    }

    private static bool PluginCategoryFilter(string? category, LogLevel logLevel)
    {
        if (category is null || !category.StartsWith(PluginCategoryPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        var configuredLevel = JellycheckrLogLevelState.GetMinimumLogLevel();
        return logLevel >= configuredLevel;
    }
}
