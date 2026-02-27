import type { PluginConfig } from '../../types';
import { CheckboxCard } from '../controls/CheckboxCard';
import { NumberField } from '../controls/NumberField';
import { SectionHeader } from '../controls/SectionHeader';
import type { NumberHandlerFactory, UpdateFieldHandler } from './types';

type DeveloperToolsSectionProps = {
  config: PluginConfig;
  onUpdateField: UpdateFieldHandler;
  onNumberInput: NumberHandlerFactory;
};

const sectionClass = 'grid gap-3 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-4 shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise';

export function DeveloperToolsSection(props: DeveloperToolsSectionProps) {
  const c = props.config;

  return (
    <section class={sectionClass}>
      <SectionHeader
        title="Developer Tools"
        description="Use fast-cycle mode to validate behavior quickly in local testing."
      />
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
