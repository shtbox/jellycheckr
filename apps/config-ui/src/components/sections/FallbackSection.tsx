import type { PluginConfig } from '../../types';
import { CheckboxCard } from '../controls/CheckboxCard';
import { NumberField } from '../controls/NumberField';
import { SectionHeader } from '../controls/SectionHeader';
import { TextField } from '../controls/TextField';
import type { NumberHandlerFactory, UpdateFieldHandler } from './types';

type FallbackSectionProps = {
  config: PluginConfig;
  showFallbackFields: boolean;
  fallbackCommand: string;
  onUpdateField: UpdateFieldHandler;
  onNumberInput: NumberHandlerFactory;
};

const sectionClass = 'grid gap-3 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-4 shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise';

export function FallbackSection(props: FallbackSectionProps) {
  const c = props.config;

  return (
    <section class={`${sectionClass} ${props.showFallbackFields ? '' : 'opacity-70'}`}>
      <SectionHeader
        title="Native-Client Fallback"
        description="Configure the server-side pause/stop path for clients that cannot render the web modal."
        badge={props.fallbackCommand}
      />

      {!props.showFallbackFields ? (
        <div class="rounded-[10px] border border-dashed border-[#ffbf75]/60 bg-[#ffbf75]/10 px-3.5 py-3 text-sm leading-[1.34] text-[#ffe7c7]">
          Server fallback is currently disabled. Enable it in Feature Toggles to edit native-client behavior.
        </div>
      ) : null}

      <div class={`grid gap-3 ${props.showFallbackFields ? '' : 'hidden'}`}>
        <div class="rounded-xl border border-[#ffbf75]/50 bg-[#ffbf75]/12 px-3.5 py-3 text-sm leading-[1.35] text-[#e8f3f1]">
          Native clients (Firestick / Android TV app) do not render the Jellycheckr modal.
          Fallback uses session telemetry and Jellyfin pause/stop commands.
        </div>

        <div class="grid gap-3 max-[920px]:grid-cols-1 md:grid-cols-2">
          <NumberField
            id="jc_fb_inactive"
            label="Fallback inactivity threshold"
            min={1}
            max={720}
            value={c.ServerFallbackInactivityMinutes}
            onInput={props.onNumberInput('ServerFallbackInactivityMinutes', 1, 720)}
            help="Required server-inferred inactivity window before fallback can trigger."
            unit="min"
          />
          <NumberField
            id="jc_fb_pause_grace"
            label="Pause grace period"
            min={5}
            max={300}
            value={c.ServerFallbackPauseGraceSeconds}
            onInput={props.onNumberInput('ServerFallbackPauseGraceSeconds', 5, 300)}
            help="If pause-before-stop is enabled, wait this long for resume/activity before Stop."
            unit="sec"
          />
        </div>

        <div class="grid gap-3 max-[920px]:grid-cols-1 md:grid-cols-2">
          <CheckboxCard
            id="jc_fb_pause"
            label="Pause before stop"
            description="Pause first, wait for resume/activity, then stop if no response."
            checked={c.ServerFallbackPauseBeforeStop}
            onChange={(checked) => props.onUpdateField('ServerFallbackPauseBeforeStop', checked)}
          />
          <CheckboxCard
            id="jc_fb_message"
            label="Send message before pause"
            description="Attempt a Jellyfin client message before pausing (support varies by client)."
            checked={c.ServerFallbackSendMessageBeforePause}
            onChange={(checked) => props.onUpdateField('ServerFallbackSendMessageBeforePause', checked)}
          />
          <CheckboxCard
            id="jc_fb_dryrun"
            label="Fallback dry run"
            description="Log fallback triggers without sending pause/stop commands."
            checked={c.ServerFallbackDryRun}
            onChange={(checked) => props.onUpdateField('ServerFallbackDryRun', checked)}
          />
        </div>

        <TextField
          id="jc_fb_message_text"
          label="Fallback client message"
          value={c.ServerFallbackClientMessage}
          onInput={(e: any) => props.onUpdateField('ServerFallbackClientMessage', String(e.currentTarget.value ?? ''))}
          help="Best-effort message sent before pause when messaging is enabled."
        />
      </div>
    </section>
  );
}
