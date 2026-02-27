import { clamp } from '../../configSchema';
import type { PluginConfig } from '../../types';
import { NumberField } from '../controls/NumberField';
import { SectionHeader } from '../controls/SectionHeader';
import { LOG_LEVEL_OPTIONS } from '../logLevels';
import type { NumberHandlerFactory, UpdateFieldHandler } from './types';

type PromptTimingSectionProps = {
  config: PluginConfig;
  triggerMode: string;
  onUpdateField: UpdateFieldHandler;
  onNumberInput: NumberHandlerFactory;
};

const sectionClass = 'grid gap-3 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-4 shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise';
const fieldCardClass = 'rounded-xl border border-white/20 bg-white/[0.03] p-3';
const fieldInputClass = 'w-full rounded-[10px] border border-white/25 bg-[#051012]/55 px-2.5 py-2 text-sm text-[#e8f3f1] outline-none transition focus:border-[#67d8bf]/75 focus:ring-2 focus:ring-[#67d8bf]/40';

export function PromptTimingSection(props: PromptTimingSectionProps) {
  const c = props.config;

  return (
    <section class={sectionClass}>
      <SectionHeader
        title="Prompt Timing"
        description="Define when prompts fire and how long the interaction window remains open."
        badge={props.triggerMode}
      />
      <div class="grid gap-3 max-[920px]:grid-cols-1 md:grid-cols-2">
        <NumberField
          id="jc_episode"
          label="Episode threshold"
          min={1}
          max={20}
          value={c.EpisodeThreshold}
          onInput={props.onNumberInput('EpisodeThreshold', 1, 20)}
          help="Used when episode checking is enabled."
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
          help="Used when timer checking is enabled."
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

        <div class={fieldCardClass}>
          <div class="mb-1.5 flex items-start justify-between gap-2.5">
            <label class="text-sm font-bold leading-[1.22] text-[#e8f3f1]" for="jc_min_log_level">Server log level</label>
          </div>
          <select
            id="jc_min_log_level"
            value={String(c.MinimumLogLevel)}
            onChange={(e: any) => props.onUpdateField('MinimumLogLevel', clamp(parseInt(e.currentTarget.value, 10), 0, 6))}
            class={fieldInputClass}
          >
            {LOG_LEVEL_OPTIONS.map((option) => (
              <option value={String(option.value)}>{option.label}</option>
            ))}
          </select>
          <div class="mt-1.5 text-[0.84rem] leading-[1.33] text-[#def0eb]/72">Minimum plugin log level for categories under Jellycheckr.Server.</div>
        </div>
      </div>
    </section>
  );
}
