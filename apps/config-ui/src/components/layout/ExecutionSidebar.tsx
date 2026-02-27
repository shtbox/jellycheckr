type ExecutionSidebarProps = {
  triggerMode: string;
  interactionQuietSeconds: number;
  promptTimeoutSeconds: number;
  cooldownMinutes: number;
  fallbackCommand: string;
  logLevelLabel: string;
  developerMode: boolean;
  developerPromptAfterSeconds: number;
};

const sideCardClass = 'grid gap-3 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-4 shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise';

export function ExecutionSidebar(props: ExecutionSidebarProps) {
  return (
    <aside class="grid gap-4 xl:sticky xl:top-1.5 xl:self-start max-[1140px]:grid-cols-2 max-[920px]:grid-cols-1">
      <section class={sideCardClass}>
        <h3 class="m-0 text-[0.96rem] font-semibold text-[#e8f3f1]">Execution Summary</h3>
        <ul class="m-0 grid list-none gap-2 p-0">
          <li class="flex items-start justify-between gap-3 border-b border-dashed border-white/15 pb-2">
            <span class="text-[0.84rem] leading-[1.32] text-[#def0eb]/72">Prompt trigger</span>
            <strong class="text-right text-[0.84rem] leading-[1.32] text-[#e8f3f1]">{props.triggerMode}</strong>
          </li>
          <li class="flex items-start justify-between gap-3 border-b border-dashed border-white/15 pb-2">
            <span class="text-[0.84rem] leading-[1.32] text-[#def0eb]/72">Quiet window</span>
            <strong class="text-right text-[0.84rem] leading-[1.32] text-[#e8f3f1]">{props.interactionQuietSeconds}s</strong>
          </li>
          <li class="flex items-start justify-between gap-3 border-b border-dashed border-white/15 pb-2">
            <span class="text-[0.84rem] leading-[1.32] text-[#def0eb]/72">Timeout</span>
            <strong class="text-right text-[0.84rem] leading-[1.32] text-[#e8f3f1]">{props.promptTimeoutSeconds}s</strong>
          </li>
          <li class="flex items-start justify-between gap-3 border-b border-dashed border-white/15 pb-2">
            <span class="text-[0.84rem] leading-[1.32] text-[#def0eb]/72">Cooldown</span>
            <strong class="text-right text-[0.84rem] leading-[1.32] text-[#e8f3f1]">{props.cooldownMinutes}m</strong>
          </li>
          <li class="flex items-start justify-between gap-3 border-b border-dashed border-white/15 pb-2">
            <span class="text-[0.84rem] leading-[1.32] text-[#def0eb]/72">Fallback command</span>
            <strong class="text-right text-[0.84rem] leading-[1.32] text-[#e8f3f1]">{props.fallbackCommand}</strong>
          </li>
          <li class="flex items-start justify-between gap-3 border-b border-dashed border-white/15 pb-2">
            <span class="text-[0.84rem] leading-[1.32] text-[#def0eb]/72">Log level</span>
            <strong class="text-right text-[0.84rem] leading-[1.32] text-[#e8f3f1]">{props.logLevelLabel}+</strong>
          </li>
          <li class="flex items-start justify-between gap-3">
            <span class="text-[0.84rem] leading-[1.32] text-[#def0eb]/72">Developer mode</span>
            <strong class="text-right text-[0.84rem] leading-[1.32] text-[#e8f3f1]">
              {props.developerMode ? `After ${Math.max(1, props.developerPromptAfterSeconds)}s` : 'Off'}
            </strong>
          </li>
        </ul>
      </section>

      <section class={`${sideCardClass} border-[#ffbf75]/35 bg-[radial-gradient(130%_100%_at_0_0,rgba(255,191,117,0.18),transparent_60%),linear-gradient(168deg,rgba(35,43,35,0.84),rgba(24,32,29,0.78))]`}>
        <h3 class="m-0 text-[0.96rem] font-semibold text-[#e8f3f1]">Operational Notes</h3>
        <p class="m-0 text-[0.84rem] leading-[1.38] text-[#def0eb]/72">
          Web players render the interactive Jellycheckr prompt. Native clients depend on server fallback logic.
        </p>
        <p class="m-0 text-[0.84rem] leading-[1.38] text-[#def0eb]/72">
          If both checks are active, the first reached threshold triggers enforcement.
        </p>
        <p class="m-0 text-[0.84rem] leading-[1.38] text-[#def0eb]/72">
          Save after any changes to apply them on the server plugin immediately.
        </p>
      </section>
    </aside>
  );
}
