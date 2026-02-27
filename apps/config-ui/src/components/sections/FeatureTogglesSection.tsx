import type { PluginConfig } from '../../types';
import { CheckboxCard } from '../controls/CheckboxCard';
import { SectionHeader } from '../controls/SectionHeader';
import type { UpdateFieldHandler } from './types';

type FeatureTogglesSectionProps = {
  config: PluginConfig;
  onUpdateField: UpdateFieldHandler;
};

const sectionClass = 'grid gap-3 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-4 shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise';

export function FeatureTogglesSection(props: FeatureTogglesSectionProps) {
  const c = props.config;

  return (
    <section class={sectionClass}>
      <SectionHeader
        title="Feature Toggles"
        description="Enable core behavior first, then choose which checks should trigger the prompt."
      />
      <div class="grid gap-3 max-[920px]:grid-cols-1 md:grid-cols-2">
        <CheckboxCard
          id="jc_enabled"
          label="Feature enabled"
          description="Master on/off switch for all checks, prompts, and server fallback actions."
          checked={c.Enabled}
          onChange={(checked) => props.onUpdateField('Enabled', checked)}
        />
        <CheckboxCard
          id="jc_fb_enabled"
          label="Enable server-side fallback"
          description="Apply native-client fallback using pause/stop flow when thresholds are reached."
          checked={c.EnableServerFallback}
          onChange={(checked) => props.onUpdateField('EnableServerFallback', checked)}
        />
        <CheckboxCard
          id="jc_episode_toggle"
          label="Enable episode checking"
          description="Count consecutive episodes and trigger once the episode threshold is met."
          checked={c.EnableEpisodeCheck}
          onChange={(checked) => props.onUpdateField('EnableEpisodeCheck', checked)}
        />
        <CheckboxCard
          id="jc_timer_toggle"
          label="Enable timer checking"
          description="Trigger based on elapsed playback/inactivity time."
          checked={c.EnableTimerCheck}
          onChange={(checked) => props.onUpdateField('EnableTimerCheck', checked)}
        />
      </div>
    </section>
  );
}
