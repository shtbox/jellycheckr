# Configuration

Schema source of truth: `packages/contracts/aysw.schema.json`

## Fields

- `enabled` (bool)
- `enableEpisodeCheck` (bool)
- `enableTimerCheck` (bool)
- `enableServerFallback` (bool)
- `episodeThreshold` (int, min 1)
- `minutesThreshold` (int, min 1)
- `interactionQuietSeconds` (int, min 5)
- `promptTimeoutSeconds` (int, min 10)
- `cooldownMinutes` (int, min 0)
- `serverFallbackInactivityMinutes` (int, min 1)
- `serverFallbackPauseBeforeStop` (bool)
- `serverFallbackPauseGraceSeconds` (int, min 5)
- `serverFallbackSendMessageBeforePause` (bool)
- `serverFallbackClientMessage` (string)
- `serverFallbackDryRun` (bool)
- `debugLogging` (bool)
- `developerMode` (bool)
- `developerPromptAfterSeconds` (int, min 1)
- `schemaVersion` (int)

## Default Values

- enabled: true
- enableEpisodeCheck: true
- enableTimerCheck: true
- enableServerFallback: true
- episodeThreshold: 3
- minutesThreshold: 120
- interactionQuietSeconds: 45
- promptTimeoutSeconds: 60
- cooldownMinutes: 30
- serverFallbackInactivityMinutes: 30
- serverFallbackPauseBeforeStop: true
- serverFallbackPauseGraceSeconds: 45
- serverFallbackSendMessageBeforePause: true
- serverFallbackClientMessage: "Are you still watching? Playback will stop soon unless you resume."
- serverFallbackDryRun: false
- debugLogging: false
- developerMode: false
- developerPromptAfterSeconds: 15
- schemaVersion: 3

## Merge Strategy

Effective config is currently:
1. global defaults from persisted plugin configuration
2. optional per-user override (if present)

Missing fields are hydrated using server defaults before response.

## Trigger Semantics

- Episode and timer checks share the same thresholds across web prompt and server fallback.
- If both checks are enabled, trigger behavior is OR (first threshold reached wins).
- If one check is disabled, only the enabled check is evaluated.

## Server Fallback Notes (Native Clients)

- `enableServerFallback=true` applies fallback behavior for stock native Jellyfin clients (for example Firestick / Android TV) where Jellycheckr cannot inject a prompt UI.
- Fallback uses server-observed playback heuristics and Jellyfin pause/stop commands.
- `serverFallbackDryRun=true` logs triggers without sending pause/stop commands (recommended for tuning).
