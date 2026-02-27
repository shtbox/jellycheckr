export function CheckboxCard(props: {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  const cardTone = props.checked
    ? 'border-[#67d8bf]/55 bg-[#67d8bf]/15'
    : 'border-white/20 bg-white/[0.03] hover:border-[#8dd8c7]/45 hover:bg-white/[0.06]';
  const switchTone = props.checked
    ? 'border-[#67d8bf]/90 bg-[#67d8bf]/55'
    : 'border-white/30 bg-white/15';
  const thumbTone = props.checked ? 'translate-x-[17px]' : 'translate-x-0';

  return (
    <label
      class={`relative grid min-h-[92px] cursor-pointer grid-cols-[auto_minmax(0,1fr)] items-start gap-3 rounded-xl border p-3 transition focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-[#67d8bf]/45 ${cardTone}`}
      for={props.id}
    >
      <div class={`flex h-6 w-[42px] items-center rounded-full border p-[2px] transition ${switchTone}`} aria-hidden="true">
        <span class={`h-[18px] w-[18px] rounded-full bg-[#f6fffd] shadow-[0_2px_4px_rgba(0,0,0,0.35)] transition-transform ${thumbTone}`} />
      </div>
      <div>
        <div class="text-sm font-bold leading-[1.26] text-[#e8f3f1]">{props.label}</div>
        <div class="mt-1 text-[0.88rem] leading-[1.33] text-[#def0eb]/72">{props.description}</div>
      </div>
      <div class="pointer-events-none absolute inset-0">
        <input
          id={props.id}
          type="checkbox"
          class="pointer-events-none absolute h-px w-px opacity-0"
          checked={props.checked}
          onChange={(e: any) => props.onChange(!!e.currentTarget.checked)}
        />
      </div>
    </label>
  );
}
