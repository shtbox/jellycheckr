# API

Base path: `/Plugins/Aysw` (alias: `/Plugins/jellycheckr`)

Authentication: standard Jellyfin auth token required for all endpoints.
Admin-only: `/admin/*`.

## GET /Plugins/Aysw/config

Returns effective config for the current user/device.

Response example:

```json
{
  "enabled": true,
  "episodeThreshold": 3,
  "minutesThreshold": 120,
  "interactionQuietSeconds": 45,
  "promptTimeoutSeconds": 60,
  "cooldownMinutes": 30,
  "enforcementMode": "WebOnly",
  "serverFallbackEpisodeThreshold": 3,
  "serverFallbackMinutesThreshold": 120,
  "serverFallbackTriggerMode": "Any",
  "serverFallbackInactivityMinutes": 30,
  "serverFallbackPauseBeforeStop": true,
  "serverFallbackPauseGraceSeconds": 45,
  "serverFallbackSendMessageBeforePause": true,
  "serverFallbackClientMessage": "Are you still watching? Playback will stop soon unless you resume.",
  "serverFallbackDryRun": false,
  "debugLogging": false,
  "developerMode": false,
  "developerPromptAfterSeconds": 15,
  "version": 2,
  "schemaVersion": 2
}
```

## POST /Plugins/Aysw/sessions/{sessionId}/ack

Body:

```json
{
  "ackType": "continue",
  "clientTimeUtc": "2026-02-22T20:05:00Z",
  "reason": "user_clicked_continue",
  "itemId": "12345",
  "clientType": "web",
  "deviceId": "browser-1"
}
```

Response:

```json
{
  "resetApplied": true,
  "nextEligiblePromptUtc": "2026-02-22T20:35:00Z"
}
```

## POST /Plugins/Aysw/sessions/{sessionId}/interaction

Body:

```json
{
  "eventType": "pointerdown",
  "clientTimeUtc": "2026-02-22T20:06:00Z",
  "itemId": "12345",
  "clientType": "web",
  "deviceId": "browser-1"
}
```

Response:

```json
{
  "accepted": true,
  "receivedAtUtc": "2026-02-22T20:06:00Z"
}
```

## POST /Plugins/Aysw/sessions/{sessionId}/prompt-shown

Body:

```json
{
  "timeoutSeconds": 60,
  "itemId": "12345",
  "clientType": "web",
  "deviceId": "browser-1"
}
```

Response: `202 Accepted`

## GET /Plugins/Aysw/admin/config

Returns global plugin defaults and behavior settings.

## PUT /Plugins/Aysw/admin/config

Accepts the same schema as admin config response.
Validates and persists global defaults.

## Native Client Behavior Note

When `enforcementMode` is `ServerFallback`, stock native clients (for example Android TV / Firestick) do not receive the Jellycheckr web modal UI. The plugin instead uses server-side heuristics and Jellyfin pause/stop commands.
