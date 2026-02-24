# Server Plugin

Jellycheckr server plugin for Jellyfin.

## Responsibilities

- Persist global configuration
- Resolve effective configuration (global + optional per-user override)
- Track minimal in-memory session state
- Expose API endpoints under `/Plugins/Aysw/*`
- Optional best-effort fallback enforcement

## Build

From repo root:

- `dotnet build apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj`
- `dotnet publish apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj -c Release -f net8.0`
- Zip artifact is written to `apps/server-plugin/artifacts/`
