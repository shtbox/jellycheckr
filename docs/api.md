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
  "enableEpisodeCheck": true,
  "enableTimerCheck": true,
  "enableServerFallback": true,
  "episodeThreshold": 3,
  "minutesThreshold": 120,
  "interactionQuietSeconds": 45,
  "promptTimeoutSeconds": 60,
  "cooldownMinutes": 30,
  "serverFallbackInactivityMinutes": 30,
  "serverFallbackPauseBeforeStop": true,
  "serverFallbackPauseGraceSeconds": 45,
  "serverFallbackSendMessageBeforePause": true,
  "serverFallbackClientMessage": "Are you still watching? Playback will stop soon unless you resume.",
  "serverFallbackDryRun": false,
  "debugLogging": false,
  "developerMode": false,
  "developerPromptAfterSeconds": 15,
  "version": 3,
  "schemaVersion": 3
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

## GET /Plugins/Aysw/web/*

Plugin-served static web assets (also available under `/Plugins/jellycheckr/web/*`):

- `/Plugins/Aysw/web/jellycheckr-web.js`
- `/Plugins/Aysw/web/jellycheckr-config-ui.js`
- `/Plugins/Aysw/web/jellycheckr-config-ui.css`
- `/Plugins/Aysw/web/jellycheckr-config-ui-host.html`

## Native Client Behavior Note

When `enableServerFallback` is `true`, stock native clients (for example Android TV / Firestick) do not receive the Jellycheckr web modal UI. The plugin instead uses server-side heuristics and Jellyfin pause/stop commands.
