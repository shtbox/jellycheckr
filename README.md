<img width="200" height="200" alt="logo" src="https://github.com/user-attachments/assets/491af40a-605a-4094-bf42-f1d4f966ec92" />

# Jellycheckr

`jellycheckr` is a monorepo for an "Are You Still Watching?" feature focused on:
- Jellyfin server plugin (`apps/server-plugin`)
- Jellyfin Web client module (`apps/web-client`)
- Embedded plugin configuration UI (`apps/config-ui`)

> [!NOTE]
> This plugin relies on the Jellyfin File Transformation plugin:
> https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/tree/main

## Web Client Example
<img width="auto" height="auto" alt="image" src="https://github.com/user-attachments/assets/5606b76f-225b-4fca-97e0-b1458db4b64d" />


## Repository Layout

- `apps/server-plugin` - .NET 8 Jellyfin plugin backend (policy/config/session API)
- `apps/web-client` - TypeScript web module (prompt UX, interactions, ack calls)
- `apps/config-ui` - Preact configuration UI embedded into the plugin
- `packages/contracts` - shared API/config contracts
- `docs` - architecture, API, configuration, and developer notes

## Quick Start

### 1) Build / publish the plugin (includes embedded web bundles)

- Build (backend + embedded web/config UI):
  - `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj`
- Publish (`net8.0`) and create plugin zip:
  - `dotnet publish apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -c Release -f net8.0`
  - Zip artifact: `apps/server-plugin/artifacts/jellycheckr-server-plugin-0.1.0.zip`

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

