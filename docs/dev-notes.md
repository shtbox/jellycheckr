# Developer Notes

## Local Development

1. Build the plugin project (includes bundled web client and config UI assets):
   - `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj`
2. Publish the plugin and create a zip artifact (`net8.0`):
   - `dotnet publish apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -c Release -f net8.0`
   - Zip output: `apps/server-plugin/artifacts/jellycheckr-server-plugin-<version>.zip`
   - The zip now includes `meta.json` (Jellyfin plugin metadata) required for reliable disable/uninstall actions in the Jellyfin UI.
3. Install the plugin zip into your local Jellyfin plugins path:
   - `pwsh ./tools/scripts/dev-install-plugin.ps1 ...`

Optional backend-only fast loop (skip frontend rebuilds):
- `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -p:JellycheckrBuildEmbeddedAssets=false`

## Repeatable Docker Harness (Pre-release Validation)

Use the harness to build plugin source against a pinned Jellyfin release image, run the container, and execute smoke checks.

Primary script:
- `pwsh ./tools/harness/scripts/Invoke-Harness.ps1`

Quick commands:
- Build only:
  - `pwsh ./tools/harness/scripts/Invoke-Harness.ps1 -Mode build -Version 10.11.6`
- Start container (build + up):
  - `pwsh ./tools/harness/scripts/Invoke-Harness.ps1 -Mode up -Version 10.11.6`
- One-command smoke run (build + up + checks + artifacts):
  - `pwsh ./tools/harness/scripts/Invoke-Harness.ps1 -Mode smoke -Version 10.11.6`
- Matrix run from `tools/harness/versions.json`:
  - `pwsh ./tools/harness/scripts/Invoke-Harness.ps1 -Mode matrix`
- Stop container:
  - `pwsh ./tools/harness/scripts/Invoke-Harness.ps1 -Mode down -Version 10.11.6`

Smoke checks include:
- Jellyfin readiness check (`/System/Info/Public`)
- Plugin web asset checks (`/Plugins/Aysw/web/*`)
  - `/Plugins/Aysw/web/jellycheckr-web.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui.css`
  - `/Plugins/Aysw/web/jellycheckr-config-ui-host.html`
  - verifies `X-Content-Type-Options: nosniff` on each asset response
- Authenticated plugin config request (`/Plugins/Aysw/config`)
- Authenticated register sanity request (`/Plugins/Aysw/web-client/register`) expecting unresolved response with `reason=session_unresolved` when no active playback session exists
- Authenticated heartbeat sanity request (`/Plugins/Aysw/web-client/heartbeat`) expecting unresolved response with `reason=session_unresolved` when no active playback session exists
- Ownership enforcement checks expecting `403`:
  - `POST /Plugins/Aysw/sessions/<foreign>/ack`
  - `POST /Plugins/Aysw/sessions/<foreign>/interaction`
  - `POST /Plugins/Aysw/sessions/<foreign>/prompt-shown`
  - `POST /Plugins/Aysw/web-client/unregister` with a foreign session id
- Developer fallback playback check:
  - copies `tools/harness/test-video.mp4` into the harness container at `/media/harness/test-video.mp4`
  - creates/refreshes a Jellyfin `HarnessMedia` library from `/media/harness`
  - enables admin `DeveloperMode=true`, `DeveloperPromptAfterSeconds=15`, `ServerFallbackDryRun=true`
  - publishes `Sessions/Playing` + `Sessions/Playing/Progress` updates and verifies the log trigger reason `developer_mode_after_15s`
- Web UI injection registration check:
  - verifies `Registered Jellyfin Web index.html transformation for web client injection` is present in logs for `10.11.x`
  - records `skipped_unsupported_jellyfin_version` for `10.9.x` because no compatible File Transformation package is pinned in harness

Artifacts:
- Written under `tools/harness/artifacts/`
- Includes smoke summary, API response payloads, and docker compose logs per run

### Version/Framework Mapping

The harness enforces the plugin framework based on Jellyfin package compatibility:
- `10.11.x` => `net9.0`
- `10.9.x` => `net8.0`

If you add a new Jellyfin minor line, update `Resolve-TargetFramework` in `tools/harness/scripts/Invoke-Harness.ps1`.

### Harness Determinism Notes

- `up` runs with `--renew-anon-volumes` so each run starts from a clean Jellyfin `/config` state.
- `down` removes anonymous volumes to prevent stale plugin binaries/config from leaking into later runs.
- Harness sets deterministic server identity per Jellyfin version:
  - fixed `ServerName` via compose hostname (`jellycheckr-harness-<version>`)
  - fixed `ServerId` via `/config/data/device.txt` (version-derived MD5 in `Invoke-Harness.ps1`)
- On some `10.11.x` starts, Jellyfin reports `StartupWizardCompleted=true` with no users. Harness auth bootstrap now forces startup wizard mode and seeds the harness credentials automatically.
- Harness image auto-installs the File Transformation plugin only for `10.11.x`; `10.9.x` runs skip this because the available package targets newer Jellyfin assemblies.

## Testing When Server Is On TrueNAS SCALE

- Keep your TrueNAS Jellyfin as the playback target.
- Enable `developerMode=true` and set `developerPromptAfterSeconds` low (for example `5`) so prompt cycles happen quickly.
- Use web browser devtools console to verify:
  - module mounted
  - config fetch succeeded
  - ack calls return success
- Validate plugin-served web assets return HTTP 200:
  - `/Plugins/Aysw/web/jellycheckr-web.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui.css`
  - `/Plugins/Aysw/web/jellycheckr-config-ui-host.html`
- If deployment to TrueNAS is cumbersome, validate behavior first in local docker Jellyfin, then deploy the same plugin zip to TrueNAS.

## Fast Prompt Toggle

Use helper script to set short-cycle test config on a reachable server:
- `pwsh ./tools/scripts/dev-enable-fast-mode.ps1 -BaseUrl http://127.0.0.1:5127 -Token dev-token -Seconds 5`

## Manual Plugin Install / "Repository" Message

If you install the server plugin manually (copy into `plugins/JellycheckrAysw`), the dashboard may show **"An error occurred while getting the plugin details from the repository."** when you open the plugin or its configure screen. That is expected: the plugin is not in the Jellyfin plugin catalog, so the dashboard's repo lookup fails. The plugin still works. Use **Configure** to open the built-in config page and change settings; nothing in the server logs is required for this.

If the Jellyfin UI shows 404s when disabling or uninstalling the plugin (`/Plugins/<id>/<version>/Disable` or `/Plugins/<id>/<version>`), verify the installed plugin folder includes `meta.json` and that its `version` matches the plugin DLL file version (for example `0.1.0.0`, not `0.1.0`).

## Plugin Debugging

- Use Jellyfin logs and filter for `Jellycheckr`.
- Enable `debugLogging` in admin config for detailed lifecycle traces.
- Standalone dev harness auth:
  - Header: `X-Emby-Token: <token>`
  - Env var: `JELLYCHECKR_DEV_TOKEN` (default `dev-token`)
  - Optional role header for admin endpoints: `X-User-Role: Administrator`

## Web Module Debugging

- Module logs are prefixed with `[jellycheckr-web]`.
- Ensure `/Plugins/Aysw/web/jellycheckr-web.js` returns HTTP 200.
- Verify prompt behavior in autoplay series playback.

## Server Fallback (Native Clients) Debugging

- Ensure `enableServerFallback=true` for stock Android TV / Firestick testing.
- Keep at least one of `enableEpisodeCheck` or `enableTimerCheck` enabled.
- Start with `serverFallbackDryRun=true` to confirm triggers before sending pause/stop commands.
- Watch Jellyfin logs for `Server fallback trigger`, `pause initiated`, and `grace expired` messages.
- Native clients do not show the Jellycheckr modal; fallback uses Jellyfin session telemetry and remote commands.

## Tested Compatibility

Track tested Jellyfin versions in release notes. Initial target:
- Jellyfin server 10.9.x+
- jellyfin-web matching server release
