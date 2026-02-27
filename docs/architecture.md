# Architecture

## Overview

`jellycheckr` v1 has two runtime components:

1. Server plugin: central policy/configuration/session tracking and optional fallback enforcement
2. Jellyfin Web module: prompt UX, interaction tracking, transition detection, and ack flow

## Component Diagram

```mermaid
flowchart LR
  U[User] --> W[Jellyfin Web Player + jellycheckr module]
  W -->|GET effective config| P[Jellycheckr Server Plugin]
  W -->|POST interaction| P
  W -->|POST ack continue/stop| P
  P -->|optional fallback| S[Jellyfin Session Control]
```

## Playback Prompt Flow

```mermaid
sequenceDiagram
  participant Web as Web Module
  participant Plugin as Server Plugin
  participant Player as Web Player

  Web->>Plugin: GET /Plugins/Aysw/config
  Plugin-->>Web: EffectiveConfigResponse
  loop during playback
    Web->>Web: Track interaction + transitions
    alt threshold reached
      Web->>Web: Show blocking modal + countdown
      alt user continues
        Web->>Plugin: POST /sessions/{id}/ack (continue)
        Plugin-->>Web: resetApplied + nextEligiblePromptUtc
        Web->>Web: Reset counters and close modal
      else stop clicked or timeout
        Web->>Player: stop()
        Web->>Plugin: POST /sessions/{id}/ack (stop|timeout)
      end
    end
  end
```

## Design Decisions

- Policy thresholds are server-owned and returned as effective config.
- Interaction quality is client-owned because server cannot infer user intent reliably.
- Session state persistence is in-memory for v1; configuration persists to disk.
- Plugin web assets are published into the plugin folder (`web/`) and served by plugin routes; only the dashboard config page shim remains embedded.
- API contracts are versioned and mirrored through `packages/contracts`.
