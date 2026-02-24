namespace Jellycheckr.Server.Infrastructure;

public static class JellycheckrWebIndexTransform
{
    private const string LoaderMarker = "id=\"jellycheckr-web-loader\"";

    public static string Transform(JellycheckrTransformationPayload payload)
    {
        var contents = payload.Contents ?? string.Empty;
        if (contents.Length == 0 || contents.Contains(LoaderMarker, StringComparison.OrdinalIgnoreCase))
        {
            return contents;
        }

        var loaderSnippet = BuildLoaderSnippet();

        var bodyCloseIndex = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyCloseIndex >= 0)
        {
            return contents.Insert(bodyCloseIndex, loaderSnippet);
        }

        var headCloseIndex = contents.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headCloseIndex >= 0)
        {
            return contents.Insert(headCloseIndex, loaderSnippet);
        }

        return contents + loaderSnippet;
    }

    private static string BuildLoaderSnippet()
    {
        var version = typeof(JellycheckrWebIndexTransform).Assembly.GetName().Version?.ToString() ?? "1";
        var relativeUrl = $"Plugins/Aysw/web/jellycheckr-web.js?v={Uri.EscapeDataString(version)}";

        return $@"
<script id=""jellycheckr-web-loader"">
(function() {{
  if (document.getElementById('jellycheckr-web-client-script')) return;
  var path = window.location.pathname || '/';
  var lowerPath = path.toLowerCase();
  var webIndex = lowerPath.indexOf('/web/');
  var rootPrefix = webIndex >= 0 ? path.slice(0, webIndex + 1) : '/';
  if (!rootPrefix) rootPrefix = '/';
  if (rootPrefix.charAt(rootPrefix.length - 1) !== '/') rootPrefix += '/';
  var src = rootPrefix + '{relativeUrl}';
  var script = document.createElement('script');
  script.id = 'jellycheckr-web-client-script';
  script.src = src;
  script.defer = true;
  document.head.appendChild(script);
}})();
</script>
";
    }
}

public sealed class JellycheckrTransformationPayload
{
    public string? Contents { get; set; }
}
