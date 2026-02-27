import type { StatusTone } from '../../types';

type HeroPanelProps = {
  enabled: boolean;
  fallbackEnabled: boolean;
  checksSummary: string;
  activeChecks: number;
  promptTimeoutSeconds: number;
  cooldownMinutes: number;
  noteTone: StatusTone;
  statusText: string;
  summaryText: string;
};

function pillToneClass(enabled: boolean): string {
  return enabled
    ? 'border-[#67d8bf]/55 bg-[#67d8bf]/20 text-[#d1fff2]'
    : 'border-[#ff9292]/55 bg-[#ff9292]/12 text-[#ffd8d8]';
}

function noteToneClass(tone: StatusTone): string {
  switch (tone) {
    case 'ok':
      return 'border-[#67d8bf]/50 bg-[#67d8bf]/12';
    case 'warn':
      return 'border-[#ffbf75]/50 bg-[#ffbf75]/12';
    case 'error':
      return 'border-[#ff9292]/50 bg-[#ff9292]/12';
    default:
      return 'border-white/25 bg-white/5';
  }
}

export function HeroPanel(props: HeroPanelProps) {
  return (
    <section class="grid gap-4 rounded-2xl border border-[#8ebebc]/25 bg-[linear-gradient(168deg,rgba(14,38,43,0.84),rgba(8,26,30,0.72))] p-[1.15rem] shadow-[0_16px_34px_rgba(0,0,0,0.26)] motion-safe:animate-jc-rise">
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p class="mb-1 text-[0.72rem] font-bold uppercase tracking-[0.14em] text-[#67d8bf]">Configuration Studio</p>
          <h2 class="m-0 text-[1.55rem] font-semibold leading-[1.16] text-[#e8f3f1] max-sm:text-[1.3rem]">Jellycheckr AYSW Control Deck</h2>
          <p class="mt-2 max-w-[70ch] text-sm leading-[1.45] text-[#def0eb]/72">
            Build the exact prompt and fallback flow used across web players and native clients.
            Thresholds, enforcement timing, logging, and test mode all live here.
          </p>
        </div>
        <div class="flex flex-wrap justify-end gap-2">
          <span class={`inline-flex items-center whitespace-nowrap rounded-full border px-2.5 py-1 text-xs font-bold ${pillToneClass(props.enabled)}`}>
            {props.enabled ? 'Feature Active' : 'Feature Paused'}
          </span>
          <span class={`inline-flex items-center whitespace-nowrap rounded-full border px-2.5 py-1 text-xs font-bold ${pillToneClass(props.fallbackEnabled)}`}>
            {props.fallbackEnabled ? 'Fallback Ready' : 'Fallback Off'}
          </span>
        </div>
      </div>

      <div class="grid gap-3 max-[920px]:grid-cols-1 md:grid-cols-2 xl:grid-cols-4">
        <article class="rounded-xl border border-white/20 bg-white/[0.03] px-3 py-3">
          <span class="mb-1 block text-[0.7rem] uppercase tracking-[0.08em] text-[#def0eb]/72">Prompt Trigger</span>
          <span class="block text-[1.03rem] font-bold leading-[1.28] text-[#e8f3f1]">{props.checksSummary}</span>
        </article>
        <article class="rounded-xl border border-white/20 bg-white/[0.03] px-3 py-3">
          <span class="mb-1 block text-[0.7rem] uppercase tracking-[0.08em] text-[#def0eb]/72">Active Checks</span>
          <span class="block text-[1.03rem] font-bold leading-[1.28] text-[#e8f3f1]">{props.activeChecks} of 2</span>
        </article>
        <article class="rounded-xl border border-white/20 bg-white/[0.03] px-3 py-3">
          <span class="mb-1 block text-[0.7rem] uppercase tracking-[0.08em] text-[#def0eb]/72">Prompt Timeout</span>
          <span class="block text-[1.03rem] font-bold leading-[1.28] text-[#e8f3f1]">{props.promptTimeoutSeconds}s</span>
        </article>
        <article class="rounded-xl border border-white/20 bg-white/[0.03] px-3 py-3">
          <span class="mb-1 block text-[0.7rem] uppercase tracking-[0.08em] text-[#def0eb]/72">Cooldown</span>
          <span class="block text-[1.03rem] font-bold leading-[1.28] text-[#e8f3f1]">{props.cooldownMinutes}m</span>
        </article>
      </div>

      <div class={`rounded-xl border px-3.5 py-3 text-sm leading-[1.35] text-[#e8f3f1] ${noteToneClass(props.noteTone)}`}>
        {props.statusText} {props.summaryText}
      </div>
    </section>
  );
}
