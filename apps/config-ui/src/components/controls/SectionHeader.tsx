export function SectionHeader(props: { title: string; description: string; badge?: string }) {
  return (
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h3 class="m-0 text-[1.04rem] font-semibold leading-[1.24] text-[#e8f3f1]">{props.title}</h3>
        <p class="mt-1.5 max-w-[68ch] text-sm leading-[1.38] text-[#def0eb]/75">{props.description}</p>
      </div>
      {props.badge ? (
        <span class="inline-flex items-center rounded-full border border-white/25 bg-white/5 px-2.5 py-1 text-xs text-[#def0eb]/80">
          {props.badge}
        </span>
      ) : null}
    </div>
  );
}
