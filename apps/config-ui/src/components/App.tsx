import {
  config,
  dirty,
  loading,
  numberHandler,
  saveConfig,
  saving,
  statusText,
  statusTone,
  summary,
  updateField
} from '../store';
import { ExecutionSidebar } from './layout/ExecutionSidebar';
import { HeroPanel } from './layout/HeroPanel';
import { getLogLevelLabel } from './logLevels';
import { DeveloperToolsSection } from './sections/DeveloperToolsSection';
import { FallbackSection } from './sections/FallbackSection';
import { FeatureTogglesSection } from './sections/FeatureTogglesSection';
import { PromptTimingSection } from './sections/PromptTimingSection';

export function App() {
  const c = config.value;
  const noteTone = statusTone.value === 'neutral' ? (dirty.value ? 'warn' : 'ok') : statusTone.value;
  const showFallbackFields = c.EnableServerFallback;
  const checksSummary = [
    c.EnableEpisodeCheck ? `${c.EpisodeThreshold} eps` : null,
    c.EnableTimerCheck ? `${c.MinutesThreshold} min` : null
  ].filter(Boolean).join(' or ') || 'Disabled';
  const activeChecks = Number(c.EnableEpisodeCheck) + Number(c.EnableTimerCheck);
  const triggerMode = !c.Enabled
    ? 'Feature disabled'
    : c.EnableEpisodeCheck && c.EnableTimerCheck
      ? 'Episode OR timer, first reached'
      : c.EnableEpisodeCheck
        ? 'Episode threshold only'
        : c.EnableTimerCheck
          ? 'Timer threshold only'
          : 'No checks selected';
  const fallbackCommand = c.EnableServerFallback
    ? (c.ServerFallbackPauseBeforeStop
      ? `Pause, wait ${c.ServerFallbackPauseGraceSeconds}s, then stop`
      : 'Direct stop command')
    : 'Fallback disabled';

  return (
    <div class="relative mx-auto grid max-w-[1240px] gap-4 py-1 text-[#e8f3f1] [font-family:'Avenir_Next','Segoe_UI_Variable_Text','Segoe_UI',Tahoma,sans-serif] [&_*]:box-border">
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-0 -z-10 rounded-[18px] bg-[radial-gradient(950px_360px_at_7%_-8%,rgba(103,216,191,0.22),transparent_68%),radial-gradient(780px_300px_at_112%_7%,rgba(255,191,117,0.16),transparent_62%)]"
      />

      <HeroPanel
        enabled={c.Enabled}
        fallbackEnabled={showFallbackFields}
        checksSummary={checksSummary}
        activeChecks={activeChecks}
        promptTimeoutSeconds={c.PromptTimeoutSeconds}
        cooldownMinutes={c.CooldownMinutes}
        noteTone={noteTone}
        statusText={statusText.value}
        summaryText={summary.value}
      />

      <div class="grid items-start gap-4 max-[1140px]:grid-cols-1 xl:grid-cols-[minmax(0,1fr)_320px]">
        <main class="min-w-0">
          <form class="grid gap-4" onSubmit={saveConfig}>
            <FeatureTogglesSection config={c} onUpdateField={updateField} />
            <PromptTimingSection
              config={c}
              triggerMode={triggerMode}
              onUpdateField={updateField}
              onNumberInput={numberHandler}
            />
            <FallbackSection
              config={c}
              showFallbackFields={showFallbackFields}
              fallbackCommand={fallbackCommand}
              onUpdateField={updateField}
              onNumberInput={numberHandler}
            />
            <DeveloperToolsSection config={c} onUpdateField={updateField} onNumberInput={numberHandler} />

            <div class="sticky bottom-2 z-[4] flex flex-wrap items-center justify-between gap-3 rounded-[14px] border border-[#67d8bf]/35 bg-[linear-gradient(145deg,rgba(20,52,58,0.94),rgba(13,33,38,0.94))] px-4 py-3 shadow-[0_14px_24px_rgba(0,0,0,0.28)] max-[640px]:static">
              <div class="flex flex-wrap items-center gap-2 text-[0.85rem] text-[#def0eb]/75">
                <span class={`inline-block h-[0.56rem] w-[0.56rem] rounded-full ${dirty.value ? 'bg-[#ffbf75]' : 'bg-[#67d8bf]'}`} />
                <span>{dirty.value ? 'Unsaved changes' : 'All changes saved'}</span>
                {loading.value ? <span>| Loading</span> : null}
              </div>
              <button
                type="submit"
                class="emby-button button-submit raised rounded-full border border-[#67d8bf]/70 bg-[linear-gradient(180deg,rgba(103,216,191,0.34),rgba(103,216,191,0.22))] px-4 py-2 font-extrabold tracking-[0.02em] text-[#eafff9] transition hover:-translate-y-px hover:brightness-105 disabled:cursor-default disabled:opacity-60 disabled:transform-none"
                disabled={saving.value || loading.value}
              >
                <span>{saving.value ? 'Saving...' : dirty.value ? 'Save Changes' : 'Save'}</span>
              </button>
            </div>
          </form>
        </main>

        <ExecutionSidebar
          triggerMode={triggerMode}
          interactionQuietSeconds={c.InteractionQuietSeconds}
          promptTimeoutSeconds={c.PromptTimeoutSeconds}
          cooldownMinutes={c.CooldownMinutes}
          fallbackCommand={fallbackCommand}
          logLevelLabel={getLogLevelLabel(c.MinimumLogLevel)}
          developerMode={c.DeveloperMode}
          developerPromptAfterSeconds={c.DeveloperPromptAfterSeconds}
        />
      </div>
    </div>
  );
}
