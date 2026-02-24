# Developer Notes

## Local Development

1. Build the plugin project (includes embedded web client and config UI bundles):
   - `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj`
2. Publish the plugin and create a zip artifact (`net8.0`):
   - `dotnet publish apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -c Release -f net8.0`
   - Zip output: `apps/server-plugin/artifacts/jellycheckr-server-plugin-<version>.zip`
3. Install the plugin zip into your local Jellyfin plugins path:
   - `pwsh ./tools/scripts/dev-install-plugin.ps1 ...`

Optional backend-only fast loop (skip embedded frontend rebuilds):
- `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -p:JellycheckrBuildEmbeddedAssets=false`

## Testing When Server Is On TrueNAS SCALE

- Keep your TrueNAS Jellyfin as the playback target.
- Enable `developerMode=true` and set `developerPromptAfterSeconds` low (for example `5`) so prompt cycles happen quickly.
- Use web browser devtools console to verify:
  - module mounted
  - config fetch succeeded
  - ack calls return success
- Validate the plugin-served embedded assets return HTTP 200:
  - `/Plugins/Aysw/web/jellycheckr-web.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui-host.html`
- If deployment to TrueNAS is cumbersome, validate behavior first in local docker Jellyfin, then deploy the same plugin zip to TrueNAS.

## Fast Prompt Toggle

Use helper script to set short-cycle test config on a reachable server:
- `pwsh ./tools/scripts/dev-enable-fast-mode.ps1 -BaseUrl http://127.0.0.1:5127 -Token dev-token -Seconds 5`

## Manual Plugin Install / "Repository" Message

If you install the server plugin manually (copy into `plugins/JellycheckrAysw`), the dashboard may show **"An error occurred while getting the plugin details from the repository."** when you open the plugin or its configure screen. That is expected: the plugin is not in the Jellyfin plugin catalog, so the dashboard's repo lookup fails. The plugin still works. Use **Configure** to open the built-in config page and change settings; nothing in the server logs is required for this.

## Plugin Debugging

- Use Jellyfin logs and filter for `Jellycheckr`.
- Enable `debugLogging` in admin config for detailed lifecycle traces.
- Standalone dev harness auth:
  - Header: `X-Emby-Token: <token>`
  - Env var: `JELLYCHECKR_DEV_TOKEN` (default `dev-token`)
  - Optional role header for admin endpoints: `X-User-Role: Administrator`

## Embedded Web Module Debugging

- Module logs are prefixed with `[jellycheckr-web]`.
- Ensure `/Plugins/Aysw/web/jellycheckr-web.js` returns HTTP 200.
- Verify prompt behavior in autoplay series playback.

## Server Fallback (Native Clients) Debugging

- Set `enforcementMode=ServerFallback` for stock Android TV / Firestick testing.
- Start with `serverFallbackDryRun=true` to confirm triggers before sending pause/stop commands.
- Watch Jellyfin logs for `Server fallback trigger`, `pause initiated`, and `grace expired` messages.
- Native clients do not show the Jellycheckr modal; fallback uses Jellyfin session telemetry and remote commands.

## Tested Compatibility

Track tested Jellyfin versions in release notes. Initial target:
- Jellyfin server 10.9.x+
- jellyfin-web matching server release
