<img width="200" height="200" alt="logo" src="https://github.com/user-attachments/assets/491af40a-605a-4094-bf42-f1d4f966ec92" />

# Jellycheckr

Jellycheckr is an "Are You Still Watching?" plugin for Jellyfin. It is built for admins who want a more deliberate playback experience: less unattended autoplay, a clearer check-in point for long binge sessions, and a fallback path for clients that cannot render a web prompt.

- Shows an interactive "Are You Still Watching?" prompt in Jellyfin Web.
- Lets you trigger the check-in by episode count, a time threshold, or both.
- Stops playback automatically if the viewer ignores the countdown.
- Uses server-side fallback actions for native clients that cannot show the modal.
- Includes a built-in configuration page inside the Jellyfin dashboard.

## Screenshots

### Built-In Configuration UI

The plugin ships with its own dashboard configuration page, so you can tune prompt timing and native-client fallback behavior without editing files.

<img width="1289" height="1300" alt="Jellycheckr configuration UI" src="https://github.com/user-attachments/assets/bcbce084-8062-4727-9848-9d9147dd9540" />

### Developer HUD (Testing Only)

If you enable developer mode, Jellycheckr can show a debug HUD to help validate timing, trigger decisions, and prompt flow during testing.

<img width="1971" height="1109" alt="Jellycheckr developer HUD" src="https://github.com/user-attachments/assets/e3514841-02eb-4cd4-8269-0debb6aa603c" />

## Why It Exists

Jellyfin does not ship with a built-in Netflix-style autoplay check-in flow. Jellycheckr adds one without forcing a single behavior across every client type.

- Web playback can present a blocking prompt with a clear continue-or-stop choice.
- Native clients can still be handled through server-observed playback and pause/stop commands.
- Admins can tune the timing to match how aggressively or gently they want playback managed.

## How It Works

### Jellyfin Web

When Jellycheckr is active in Jellyfin Web, it can show a blocking prompt with a countdown when a configured threshold is reached.

- The viewer can choose to continue watching.
- The viewer can choose to stop playback.
- If the countdown expires, Jellycheckr stops playback automatically.

### Native And Non-Web Clients

Native clients do not render the Jellycheckr modal. When server-side fallback is enabled, Jellycheckr watches session activity from the server and uses Jellyfin pause/stop commands instead.

- It can optionally send a client message first.
- It can pause before stopping.
- It can wait through a grace period before sending stop.

### Trigger Behavior

Jellycheckr uses shared trigger settings for both the web prompt and server fallback.

- Episode threshold and timer threshold are both available.
- If both checks are enabled, the first threshold reached triggers the prompt or fallback.
- After a viewer chooses `Continue`, counters reset and the cooldown delays the next prompt.
- Developer mode is available for fast testing, but it is not intended for normal usage.

## Installation

1. In Jellyfin, open `Dashboard > Plugins > Repositories`.
2. Add `https://shtbox.io/jellycheckr/manifest.json` as a custom plugin repository.
3. Open `Catalog`, find `Jellycheckr AYSW`, and install it.
4. Open the plugin configuration page from the dashboard to adjust the behavior.

> [!IMPORTANT]
> If you want the interactive popup inside Jellyfin Web, install the Jellyfin File Transformation plugin as well.
> Without it, the injected Jellyfin Web modal will not appear.
> Jellycheckr can still operate in fallback-only mode for native clients when server fallback is enabled.

## Recommended First-Time Setup

Start simple and tune from real usage.

- Leave the feature enabled.
- Keep both episode and timer checks on unless you specifically want only one trigger style.
- Start with the defaults:
  - `3` episodes
  - `120` minutes
  - `60` second prompt timeout
  - `30` minute cooldown
- Leave server-side fallback enabled if you want coverage on native clients such as Android TV or Firestick.
- If you are testing native-client behavior, turn on fallback dry run first so you can confirm triggers without actually pausing or stopping playback.

## Configuration Guide

### Feature Toggles

These decide which parts of Jellycheckr are active.

- `Feature enabled`: Master switch for all prompt and fallback behavior.
- `Enable server-side fallback`: Lets Jellycheckr manage native clients that cannot show the web prompt.
- `Enable episode checking`: Triggers after a configured number of consecutive episode transitions.
- `Enable timer checking`: Triggers based on the shared time threshold used by Jellycheckr.

### Prompt Timing

These control when Jellycheckr steps in and how long the viewer has to respond.

- `Episode threshold`: How many consecutive episodes can play before prompting.
- `Timer threshold`: The shared time threshold used by the web prompt and native fallback logic.
- `Interaction quiet window`: How long Jellycheckr treats the session as inactive before counting it as no interaction.
- `Prompt timeout`: How long the viewer has before the prompt times out.
- `Cooldown`: How long Jellycheckr waits after `Continue` before it can prompt again.

The current default behavior is:

- Episode threshold: `3`
- Timer threshold: `120` minutes
- Interaction quiet window: `45` seconds
- Prompt timeout: `60` seconds
- Cooldown: `30` minutes

### Native-Client Fallback

These settings control what Jellycheckr does on clients that cannot render the modal.

- `Fallback inactivity threshold`: Requires a period of inactivity before fallback can trigger.
- `Pause before stop`: Pauses first, then stops later if the session does not recover.
- `Pause grace period`: How long to wait after pause before sending stop.
- `Send message before pause`: Attempts to send a Jellyfin client message before pausing.
- `Fallback client message`: The message text to send when messaging is enabled.
- `Fallback dry run`: Logs the trigger path without sending pause or stop commands.

### Developer Tools

These are for testing only.

- `Developer mode`: Fast-cycle trigger mode for validation, not normal playback use.
- `Developer prompt after`: A short test timer used only while developer mode is enabled.

For normal use, leave developer mode off.

## Compatibility And Limitations

- Jellycheckr currently targets Jellyfin `10.9.x`.
- The interactive Jellyfin Web prompt depends on the Jellyfin File Transformation plugin being installed and available.
- Native clients do not show the Jellycheckr modal. They rely on server fallback behavior instead.
- Client messaging before pause is best-effort and may not behave the same across all clients.
- This project is still in active development, so behavior and packaging may continue to change between releases.

## Troubleshooting

### I Installed It But Do Not See The Popup In Jellyfin Web

- Make sure the Jellyfin File Transformation plugin is installed.
- Confirm Jellycheckr is enabled in its configuration page.
- If needed, verify these plugin-served assets load successfully:
  - `/Plugins/Aysw/web/jellycheckr-web.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui.js`
  - `/Plugins/Aysw/web/jellycheckr-config-ui.css`
  - `/Plugins/Aysw/web/jellycheckr-config-ui-host.html`

### Native Clients Are Not Showing A Prompt

That is expected. Native clients do not render the Jellycheckr modal.

- Leave `Enable server-side fallback` on if you want Jellycheckr to enforce behavior there.
- Use the fallback settings to control pause, stop, grace period, and inactivity handling.

### I Want To Test Without Actually Stopping Playback

- Turn on `Fallback dry run`.
- Leave developer mode off unless you are intentionally doing fast-cycle testing.

### The Plugin Is Too Aggressive Or Not Aggressive Enough

Tune these first:

- `Episode threshold`
- `Timer threshold`
- `Fallback inactivity threshold`
- `Cooldown`

## For Developers

The root README is intentionally focused on Jellyfin admins and plugin usage.

For development, packaging, and release workflow details, see:

- `docs/dev-notes.md`
- `apps/server-plugin/README.md`
- `apps/web-client/README.md`
- `docs/config.md`
