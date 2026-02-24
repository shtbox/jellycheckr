# Configuration

Schema source of truth: `packages/contracts/aysw.schema.json`

## Fields

- `enabled` (bool)
- `episodeThreshold` (int, min 1)
- `minutesThreshold` (int, min 1)
- `interactionQuietSeconds` (int, min 5)
- `promptTimeoutSeconds` (int, min 10)
- `cooldownMinutes` (int, min 0)
- `enforcementMode` (`None` | `WebOnly` | `ServerFallback`)
- `serverFallbackEpisodeThreshold` (int, min 0; `0` disables episode threshold)
- `serverFallbackMinutesThreshold` (int, min 0; `0` disables minutes threshold)
- `serverFallbackTriggerMode` (`Any` | `All`)
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
- episodeThreshold: 3
- minutesThreshold: 120
- interactionQuietSeconds: 45
- promptTimeoutSeconds: 60
- cooldownMinutes: 30
- enforcementMode: WebOnly
- serverFallbackEpisodeThreshold: 3
- serverFallbackMinutesThreshold: 120
- serverFallbackTriggerMode: Any
- serverFallbackInactivityMinutes: 30
- serverFallbackPauseBeforeStop: true
- serverFallbackPauseGraceSeconds: 45
- serverFallbackSendMessageBeforePause: true
- serverFallbackClientMessage: "Are you still watching? Playback will stop soon unless you resume."
- serverFallbackDryRun: false
- debugLogging: false
- developerMode: false
- developerPromptAfterSeconds: 15
- schemaVersion: 2

## Merge Strategy

Effective config is currently:
1. global defaults from persisted plugin configuration
2. optional per-user override (if present)

Missing fields are hydrated using server defaults before response.

## ServerFallback Notes (Native Clients)

- `ServerFallback` is for stock native Jellyfin clients (for example Firestick / Android TV) where Jellycheckr cannot inject a prompt UI.
- Fallback uses server-observed playback heuristics and Jellyfin pause/stop commands.
- `serverFallbackDryRun=true` logs triggers without sending pause/stop commands (recommended for tuning).
