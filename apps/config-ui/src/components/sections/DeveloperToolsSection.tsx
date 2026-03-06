import type { PluginConfig } from '../../types';
import { CheckboxCard } from '../controls/CheckboxCard';
import { NumberField } from '../controls/NumberField';
import { SectionHeader } from '../controls/SectionHeader';
import type { NumberHandlerFactory, UpdateFieldHandler } from './types';
import { LOG_LEVEL_OPTIONS } from '../logLevels';
import { clamp } from '../../configSchema';

type DeveloperToolsSectionProps = {
  config: PluginConfig;
  onUpdateField: UpdateFieldHandler;
  onNumberInput: NumberHandlerFactory;
};

const sectionClass = 'grid gap-3 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-4 shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise';
const fieldCardClass = 'rounded-xl border border-white/20 bg-white/[0.03] p-3';
const fieldInputClass = 'w-full rounded-[10px] border border-white/25 bg-[#051012]/55 px-2.5 py-2 text-sm text-[#e8f3f1] outline-none transition focus:border-[#67d8bf]/75 focus:ring-2 focus:ring-[#67d8bf]/40';

export function DeveloperToolsSection(props: DeveloperToolsSectionProps) {
  const c = props.config;

  return (
    <section class={sectionClass}>
      <SectionHeader
        title="Developer Tools"
        description="Use fast-cycle mode to validate behavior quickly in local testing."
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
      <div class="grid gap-3">
        <CheckboxCard
          id="jc_dev_mode"
          label="Developer mode"
          description="Fast-cycle trigger mode for testing. Keep this disabled during normal usage."
          checked={c.DeveloperMode}
          onChange={(checked) => props.onUpdateField('DeveloperMode', checked)}
        />
      </div>

      <div class={`grid gap-3 ${c.DeveloperMode ? '' : 'hidden'}`}>
        <NumberField
          id="jc_dev_after"
          label="Developer prompt after"
          min={1}
          max={60}
          value={c.DeveloperPromptAfterSeconds}
          onInput={props.onNumberInput('DeveloperPromptAfterSeconds', 1, 60)}
          help="Used only while developer mode is enabled."
          unit="sec"
        />
      </div>
    </section>
  );
}
