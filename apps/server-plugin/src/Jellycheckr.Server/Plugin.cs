using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellycheckr.Server;

public sealed class Plugin : BasePlugin<Models.PluginConfig>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        ApplicationPaths = applicationPaths;
    }

    public override string Name => "Jellycheckr AYSW";

    public override Guid Id => Guid.Parse("a53af988-9d8f-4a7c-8d5f-f902fd90e4bd");

    public static Plugin? Instance { get; private set; }

    public static IApplicationPaths? ApplicationPaths { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}
