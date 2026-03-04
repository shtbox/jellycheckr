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
  if (window.__jellycheckrWebLoaderInitialized) {{
    if (window.JellycheckrAysw && typeof window.JellycheckrAysw.remountNow === 'function') {{
      window.JellycheckrAysw.remountNow();
    }}
    return;
  }}

  window.__jellycheckrWebLoaderInitialized = true;

  var retryDelays = [1000, 3000, 10000, 30000];
  var failureCount = 0;
  var retryTimerId = 0;
  var scriptId = 'jellycheckr-web-client-script';

  function resolveRootPrefix() {{
    var path = window.location.pathname || '/';
    var lowerPath = path.toLowerCase();
    var webIndex = lowerPath.indexOf('/web/');
    var rootPrefix = webIndex >= 0 ? path.slice(0, webIndex + 1) : '/';
    if (!rootPrefix) rootPrefix = '/';
    if (rootPrefix.charAt(rootPrefix.length - 1) !== '/') rootPrefix += '/';
    return rootPrefix;
  }}

  function resolveScriptSrc() {{
    return resolveRootPrefix() + '{relativeUrl}';
  }}

  function clearRetry() {{
    if (!retryTimerId) return;
    window.clearTimeout(retryTimerId);
    retryTimerId = 0;
  }}

  function scheduleRetry() {{
    if (retryTimerId) return;
    var retryIndex = Math.min(Math.max(failureCount - 1, 0), retryDelays.length - 1);
    var delay = retryDelays[retryIndex];
    retryTimerId = window.setTimeout(function() {{
      retryTimerId = 0;
      ensureRuntime('retry');
    }}, delay);
  }}

  function notifyRuntimeLoaded() {{
    clearRetry();
    failureCount = 0;
    if (window.JellycheckrAysw && typeof window.JellycheckrAysw.remountNow === 'function') {{
      window.JellycheckrAysw.remountNow();
    }}
  }}

  function ensureRuntime(reason) {{
    if (window.JellycheckrAysw) {{
      notifyRuntimeLoaded();
      return;
    }}

    var existing = document.getElementById(scriptId);
    if (existing && existing.getAttribute('data-jellycheckr-state') === 'loading') {{
      return;
    }}

    if (existing && existing.parentNode) {{
      existing.parentNode.removeChild(existing);
    }}

    var script = document.createElement('script');
    script.id = scriptId;
    script.src = resolveScriptSrc();
    script.async = true;
    script.setAttribute('data-jellycheckr-state', 'loading');
    script.addEventListener('load', function() {{
      script.setAttribute('data-jellycheckr-state', 'loaded');
      if (window.JellycheckrAysw) {{
        notifyRuntimeLoaded();
        return;
      }}

      failureCount += 1;
      scheduleRetry();
    }});
    script.addEventListener('error', function() {{
      script.setAttribute('data-jellycheckr-state', 'error');
      failureCount += 1;
      scheduleRetry();
    }});

    var parent = document.head || document.documentElement || document.body;
    if (!parent) {{
      failureCount += 1;
      scheduleRetry();
      return;
    }}

    parent.appendChild(script);
  }}

  window.addEventListener('pageshow', function() {{ ensureRuntime('pageshow'); }});
  document.addEventListener('visibilitychange', function() {{
    if (document.visibilityState === 'visible') {{
      ensureRuntime('visibilitychange');
    }}
  }});
  window.setInterval(function() {{ ensureRuntime('interval'); }}, 30000);

  ensureRuntime('initial');
}})();
</script>
";
    }
}

public sealed class JellycheckrTransformationPayload
{
    public string? Contents { get; set; }
}
