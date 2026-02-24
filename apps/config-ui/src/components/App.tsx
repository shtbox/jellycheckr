import { clamp } from '../configSchema';
import {
  config,
  dirty,
  loading,
  modeLabel,
  numberHandler,
  saveConfig,
  saving,
  statusText,
  statusTone,
  summary,
  updateField
} from '../store';

function CheckboxCard(props: {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <div class="jc-check">
      <div class="checkboxContainer checkboxContainer-withDescription">
        <input
          id={props.id}
          type="checkbox"
          checked={props.checked}
          onChange={(e: any) => props.onChange(!!e.currentTarget.checked)}
        />
        <label for={props.id}>{props.label}</label>
        <div class="fieldDescription">{props.description}</div>
      </div>
    </div>
  );
}

function NumberField(props: {
  id: string;
  label: string;
  min: number;
  max: number;
  value: number;
  help: string;
  onInput: (ev: any) => void;
}) {
  return (
    <div class="jc-field">
      <label for={props.id}>{props.label}</label>
      <input id={props.id} type="number" min={String(props.min)} max={String(props.max)} value={props.value} onInput={props.onInput} />
      <div class="jc-help">{props.help}</div>
    </div>
  );
}

function TextField(props: {
  id: string;
  label: string;
  value: string;
  help: string;
  onInput: (ev: any) => void;
}) {
  return (
    <div class="jc-field">
      <label for={props.id}>{props.label}</label>
      <input id={props.id} type="text" value={props.value} onInput={props.onInput} />
      <div class="jc-help">{props.help}</div>
    </div>
  );
}

export function App() {
  const c = config.value;
  const enabledTone = c.Enabled ? 'ok' : 'off';
  const noteTone = statusTone.value === 'neutral' ? (dirty.value ? 'warn' : 'ok') : statusTone.value;
  const modeHint =
    c.EnforcementMode === 2 ? 'Native-client fallback uses server-side heuristics and pause/stop commands (no in-app prompt).' :
    c.EnforcementMode === 1 ? 'Web client shows and handles the prompt.' :
    'No enforcement fallback.';
  const showFallbackFields = c.EnforcementMode === 2;
  const fallbackThresholdHelp =
    c.ServerFallbackTriggerMode === 1
      ? 'Trigger when both enabled thresholds are met (episode AND minutes). Set a threshold to 0 to disable it.'
      : 'Trigger when either enabled threshold is met (episode OR minutes). Set a threshold to 0 to disable it.';

  return (
    <div class="jc-wrap">
      <section class="jc-card jc-hero">
        <div class="jc-cardBody">
          <h2 class="jc-title">Jellycheckr AYSW</h2>
          <p class="jc-sub">
            Are You Still Watching? prompt after configured episode or time thresholds.
            Bundled UI (Preact + Signals) for stable styling and interactivity.
          </p>
          <div class="jc-grid3">
            <div class="jc-stat">
              <span class="jc-statLabel">Status</span>
              <span class="jc-statValue">
                <span class={`jc-pill ${enabledTone}`}>{c.Enabled ? 'Enabled' : 'Disabled'}</span>
              </span>
            </div>
            <div class="jc-stat">
              <span class="jc-statLabel">Trigger</span>
              <span class="jc-statValue">{c.EpisodeThreshold} eps or {c.MinutesThreshold} min</span>
            </div>
            <div class="jc-stat">
              <span class="jc-statLabel">Mode</span>
              <span class="jc-statValue">{modeLabel.value}</span>
            </div>
          </div>
          <div class={`jc-note ${noteTone}`}>{statusText.value} {summary.value}</div>
        </div>
      </section>

      <section class="jc-card">
        <div class="jc-cardBody">
          <form class="jc-form" onSubmit={saveConfig}>
            <div class="jc-checkGrid">
              <CheckboxCard
                id="jc_enabled"
                label="Feature enabled"
                description="Show the AYSW prompt when thresholds are reached."
                checked={c.Enabled}
                onChange={(checked) => updateField('Enabled', checked)}
              />
              <CheckboxCard
                id="jc_debug"
                label="Debug logging"
                description="Enable extra plugin logs for support/troubleshooting."
                checked={c.DebugLogging}
                onChange={(checked) => updateField('DebugLogging', checked)}
              />
            </div>

            <hr class="jc-divider" />

            <div class="jc-row2">
              <NumberField
                id="jc_episode"
                label="Episode threshold"
                min={1}
                max={20}
                value={c.EpisodeThreshold}
                onInput={numberHandler('EpisodeThreshold', 1, 20)}
                help="Prompt after this many consecutive episodes (default 3)."
              />
              <NumberField
                id="jc_minutes"
                label="Minutes threshold"
                min={1}
                max={600}
                value={c.MinutesThreshold}
                onInput={numberHandler('MinutesThreshold', 1, 600)}
                help="Prompt after this many playback minutes (default 120)."
              />
              <NumberField
                id="jc_quiet"
                label="Interaction quiet seconds"
                min={5}
                max={300}
                value={c.InteractionQuietSeconds}
                onInput={numberHandler('InteractionQuietSeconds', 5, 300)}
                help="No-input window that counts as no interaction."
              />
              <NumberField
                id="jc_timeout"
                label="Prompt timeout (seconds)"
                min={10}
                max={300}
                value={c.PromptTimeoutSeconds}
                onInput={numberHandler('PromptTimeoutSeconds', 10, 300)}
                help="Auto-stop countdown when no response is received."
              />
              <NumberField
                id="jc_cooldown"
                label="Cooldown (minutes)"
                min={0}
                max={1440}
                value={c.CooldownMinutes}
                onInput={numberHandler('CooldownMinutes', 0, 1440)}
                help="Delay before prompting again after Continue."
              />

              <div class="jc-field">
                <label for="jc_mode">Enforcement mode</label>
                <select
                  id="jc_mode"
                  value={String(c.EnforcementMode)}
                  onChange={(e: any) => updateField('EnforcementMode', clamp(parseInt(e.currentTarget.value, 10), 0, 2))}
                >
                  <option value="0">None</option>
                  <option value="1">WebOnly</option>
                  <option value="2">ServerFallback</option>
                </select>
                <div class="jc-help">{modeHint}</div>
              </div>
            </div>

            <div class={`jc-stack ${showFallbackFields ? '' : 'jc-hidden'}`}>
              <div class="jc-note warn">
                Native clients (Firestick / Android TV app) do not render the Jellycheckr modal UI.
                Server Fallback uses Jellyfin session telemetry plus remote pause/stop commands. Message display support varies by client.
              </div>

              <div class="jc-row2">
                <NumberField
                  id="jc_fb_episode"
                  label="Fallback episode threshold"
                  min={0}
                  max={20}
                  value={c.ServerFallbackEpisodeThreshold}
                  onInput={numberHandler('ServerFallbackEpisodeThreshold', 0, 20)}
                  help="Consecutive item transitions before fallback. Use 0 to disable this threshold."
                />
                <NumberField
                  id="jc_fb_minutes"
                  label="Fallback minutes threshold"
                  min={0}
                  max={720}
                  value={c.ServerFallbackMinutesThreshold}
                  onInput={numberHandler('ServerFallbackMinutesThreshold', 0, 720)}
                  help="Playback minutes since last reset. Use 0 to disable this threshold."
                />
                <div class="jc-field">
                  <label for="jc_fb_trigger_mode">Fallback trigger mode</label>
                  <select
                    id="jc_fb_trigger_mode"
                    value={String(c.ServerFallbackTriggerMode)}
                    onChange={(e: any) => updateField('ServerFallbackTriggerMode', clamp(parseInt(e.currentTarget.value, 10), 0, 1))}
                  >
                    <option value="0">Any (OR)</option>
                    <option value="1">All (AND)</option>
                  </select>
                  <div class="jc-help">{fallbackThresholdHelp}</div>
                </div>
                <NumberField
                  id="jc_fb_inactive"
                  label="Fallback inactivity (minutes)"
                  min={1}
                  max={720}
                  value={c.ServerFallbackInactivityMinutes}
                  onInput={numberHandler('ServerFallbackInactivityMinutes', 1, 720)}
                  help="Required server-inferred inactivity window before fallback can trigger."
                />
                <NumberField
                  id="jc_fb_pause_grace"
                  label="Pause grace (seconds)"
                  min={5}
                  max={300}
                  value={c.ServerFallbackPauseGraceSeconds}
                  onInput={numberHandler('ServerFallbackPauseGraceSeconds', 5, 300)}
                  help="If pause-before-stop is enabled, wait this long for resume/activity before sending Stop."
                />
              </div>

              <div class="jc-checkGrid">
                <CheckboxCard
                  id="jc_fb_pause"
                  label="Pause before stop"
                  description="Netflix-style fallback: pause first, wait for activity/resume, then stop if no response."
                  checked={c.ServerFallbackPauseBeforeStop}
                  onChange={(checked) => updateField('ServerFallbackPauseBeforeStop', checked)}
                />
                <CheckboxCard
                  id="jc_fb_message"
                  label="Send message before pause"
                  description="Attempt a Jellyfin client message before pausing (some clients may ignore it)."
                  checked={c.ServerFallbackSendMessageBeforePause}
                  onChange={(checked) => updateField('ServerFallbackSendMessageBeforePause', checked)}
                />
                <CheckboxCard
                  id="jc_fb_dryrun"
                  label="Fallback dry run"
                  description="Log fallback triggers without sending pause/stop commands. Useful for tuning thresholds."
                  checked={c.ServerFallbackDryRun}
                  onChange={(checked) => updateField('ServerFallbackDryRun', checked)}
                />
              </div>

              <TextField
                id="jc_fb_message_text"
                label="Fallback client message"
                value={c.ServerFallbackClientMessage}
                onInput={(e: any) => updateField('ServerFallbackClientMessage', String(e.currentTarget.value ?? ''))}
                help="Best-effort message sent before pause when enabled."
              />
            </div>

            <hr class="jc-divider" />

            <div class="jc-checkGrid">
              <CheckboxCard
                id="jc_dev_mode"
                label="Developer mode"
                description="Show a quick prompt for testing. Keep this off in normal use."
                checked={c.DeveloperMode}
                onChange={(checked) => updateField('DeveloperMode', checked)}
              />

              <div class={`jc-field ${c.DeveloperMode ? '' : 'jc-hidden'}`}>
                <label for="jc_dev_after">Developer prompt after (seconds)</label>
                <input
                  id="jc_dev_after"
                  type="number"
                  min="1"
                  max="60"
                  value={c.DeveloperPromptAfterSeconds}
                  onInput={numberHandler('DeveloperPromptAfterSeconds', 1, 60)}
                />
                <div class="jc-help">Only used when developer mode is enabled.</div>
              </div>
            </div>

            <div class="jc-actions">
              <div class="jc-meta">
                {dirty.value ? 'Unsaved changes' : 'No unsaved changes'}
                {loading.value ? ' | Loading' : ''}
              </div>
              <button type="submit" class="raised button-submit emby-button" disabled={saving.value || loading.value}>
                <span>{saving.value ? 'Saving...' : dirty.value ? 'Save Changes' : 'Save'}</span>
              </button>
            </div>
          </form>
        </div>
      </section>
    </div>
  );
}
