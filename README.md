<img width="200" height="200" alt="logo" src="https://github.com/user-attachments/assets/491af40a-605a-4094-bf42-f1d4f966ec92" />

# Jellycheckr
`jellycheckr` is a monorepo for an "Are You Still Watching?" feature focused on:
- Jellyfin server plugin (`apps/server-plugin`)
- Jellyfin Web client module (`apps/web-client`)
- Plugin configuration UI (`apps/config-ui`)

## Installation
1. Add `https://shtbox.io/jellycheckr/manifest.json` as a plugin source repository on your Jellyfin server.
2. Find "Jellycheckr AYSW" in the list and install it. Configuration options available in plugin settings.


> [!NOTE]
> This plugin relies on the Jellyfin File Transformation plugin for web clients if you wish to use the "pretty" popup:
> https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/tree/main
> When this plugin is missing it will fallback to Server Side Only mode which will pause play and use the SendMessage command to the client inform the user, and stopping after the configured fallback wait time. 

## Web Client Example
<img width="auto" height="auto" alt="image" src="https://github.com/user-attachments/assets/5606b76f-225b-4fca-97e0-b1458db4b64d" />


## Repository Layout

- `apps/server-plugin` - .NET 8 Jellyfin plugin backend (policy/config/session API)
- `apps/web-client` - TypeScript web module (prompt UX, interactions, ack calls)
- `apps/config-ui` - Preact + Tailwind configuration UI published into plugin `web/` assets
- `packages/contracts` - shared API/config contracts
- `docs` - architecture, API, configuration, and developer notes

## Quick Start

### 1) Build / publish the plugin (includes plugin-folder web assets)

- Build (backend + web/config UI bundles):
  - `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj`
- Publish (`net8.0`) and create plugin zip:
  - `dotnet publish apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -c Release -f net8.0`
  - Zip artifact: `apps/server-plugin/artifacts/jellycheckr-server-plugin-0.1.0.zip`
  - The packaged zip includes Jellyfin plugin metadata (`meta.json`) and plugin `web/` assets (`jellycheckr-web.js`, config UI host/js/css).

### 2) Install for local dev

- Plugin zip install helper:
  - `pwsh ./tools/scripts/dev-install-plugin.ps1 -PluginZipPath <path-to-zip> -JellyfinPluginsDir <jellyfin-plugins-dir>`
- Fast prompt test mode:
  - `pwsh ./tools/scripts/dev-enable-fast-mode.ps1 -BaseUrl <server-url> -Token <admin-token> -Seconds 5`

See `docs/dev-notes.md` for a full local workflow.

## Versioning

Repository/plugin version is currently `0.1.0`.
Override during build/publish with `/p:Version=<version>`.

## Compatibility

This v1 implementation is designed for Jellyfin server/plugin patterns and Jellyfin Web.
Validated compatibility is documented in `docs/dev-notes.md`.

## Debugging Mode
### HUD Debug Display
<img width="1971" height="1109" alt="image" src="https://github.com/user-attachments/assets/e3514841-02eb-4cd4-8269-0debb6aa603c" />

### Configuration 
<img width="1289" height="1300" alt="image" src="https://github.com/user-attachments/assets/bcbce084-8062-4727-9848-9d9147dd9540" />


