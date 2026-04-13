
import type { PluginConfig } from '../../types';
import { NumberField } from '../controls/NumberField';
import { SectionHeader } from '../controls/SectionHeader';
import type { NumberHandlerFactory, UpdateFieldHandler } from './types';

type PromptTimingSectionProps = {
  config: PluginConfig;
  triggerMode: string;
  onUpdateField: UpdateFieldHandler;
  onNumberInput: NumberHandlerFactory;
};

const sectionClass = 'grid gap-3 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-4 shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise';
export function PromptTimingSection(props: PromptTimingSectionProps) {
  const c = props.config;

  return (
    <section class={sectionClass}>
      <SectionHeader
        title="Prompt Timing"
        description="Set when reminders appear and how long viewers have to respond."
        badge={props.triggerMode}
      />
      <div class="rounded-xl border border-[#ffbf75]/50 bg-[#ffbf75]/12 px-3.5 py-3 text-sm leading-[1.38] text-[#ffe7c7]">
        Episode-based reminders are for series playback. Movies never move the episode count, so use the timer if you want reminders during films.
      </div>
      <div class="grid gap-3 max-[920px]:grid-cols-1 md:grid-cols-2">
        <NumberField
          id="jc_episode"
          label="Episode threshold"
          min={1}
          max={20}
          value={c.EpisodeThreshold}
          onInput={props.onNumberInput('EpisodeThreshold', 1, 20)}
          help="Counts consecutive episodes from the same series when episode checking is on."
          unit="eps"
          disabled={!c.EnableEpisodeCheck}
        />
        <NumberField
          id="jc_minutes"
          label="Timer threshold"
          min={1}
          max={600}
          value={c.MinutesThreshold}
          onInput={props.onNumberInput('MinutesThreshold', 1, 600)}
          help="Applies to all playback, including movies, whenever timer checking is on."
          unit="min"
          disabled={!c.EnableTimerCheck}
        />
        <NumberField
          id="jc_quiet"
          label="Interaction quiet window"
          min={5}
          max={300}
          value={c.InteractionQuietSeconds}
          onInput={props.onNumberInput('InteractionQuietSeconds', 5, 300)}
          help="No-input period treated as no interaction."
          unit="sec"
        />
        <NumberField
          id="jc_timeout"
          label="Prompt timeout"
          min={10}
          max={300}
          value={c.PromptTimeoutSeconds}
          onInput={props.onNumberInput('PromptTimeoutSeconds', 10, 300)}
          help="Auto-stop countdown when no response is received."
          unit="sec"
        />
        <NumberField
          id="jc_cooldown"
          label="Cooldown"
          min={0}
          max={1440}
          value={c.CooldownMinutes}
          onInput={props.onNumberInput('CooldownMinutes', 0, 1440)}
          help="Delay before prompting again after Continue."
          unit="min"
        />
      </div>
    </section>
  );
}
