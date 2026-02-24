using Jellycheckr.Server.Infrastructure;
using Jellycheckr.Server.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellycheckr.Server;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IClock, SystemClock>();
        serviceCollection.AddSingleton<IConfigService, ConfigService>();
        serviceCollection.AddSingleton<ISessionStateStore, SessionStateStore>();
        serviceCollection.AddSingleton<IAckService, AckService>();
        serviceCollection.AddSingleton<IServerFallbackDecisionEngine, ServerFallbackDecisionEngine>();
        serviceCollection.AddSingleton<IServerFallbackSessionSnapshotProvider, ServerFallbackSessionSnapshotProvider>();
        serviceCollection.AddSingleton<IJellyfinSessionCommandDispatcher, JellyfinSessionCommandDispatcher>();
        serviceCollection.AddHostedService<WebUiInjectionRegistrationService>();
        serviceCollection.AddHostedService<ServerFallbackEnforcementService>();
    }
}
