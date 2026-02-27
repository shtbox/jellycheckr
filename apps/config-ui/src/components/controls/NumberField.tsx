const FIELD_CARD_CLASS = 'rounded-xl border border-white/20 bg-white/[0.03] p-3';
const FIELD_INPUT_CLASS = 'w-full rounded-[10px] border border-white/25 bg-[#051012]/55 px-2.5 py-2 text-sm text-[#e8f3f1] outline-none transition focus:border-[#67d8bf]/75 focus:ring-2 focus:ring-[#67d8bf]/40 disabled:cursor-not-allowed';

export function NumberField(props: {
  id: string;
  label: string;
  min: number;
  max: number;
  value: number;
  help: string;
  unit?: string;
  onInput: (ev: any) => void;
  disabled?: boolean;
}) {
  const rangeLabel = props.unit
    ? `${props.min}-${props.max} ${props.unit}`
    : `${props.min}-${props.max}`;

  return (
    <div class={`${FIELD_CARD_CLASS} ${props.disabled ? 'opacity-60' : ''}`}>
      <div class="mb-1.5 flex items-start justify-between gap-2.5">
        <label class="text-sm font-bold leading-[1.22] text-[#e8f3f1]" for={props.id}>{props.label}</label>
        <span class="whitespace-nowrap rounded-full border border-white/25 px-2 py-0.5 text-[0.72rem] text-[#def0eb]/75">{rangeLabel}</span>
      </div>
      <div class="relative">
        <input
          id={props.id}
          type="number"
          min={String(props.min)}
          max={String(props.max)}
          value={props.value}
          onInput={props.onInput}
          disabled={props.disabled}
          class={`${FIELD_INPUT_CLASS} ${props.unit ? 'pr-12' : ''}`}
        />
        {props.unit ? (
          <span class="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-xs text-[#def0eb]/72">{props.unit}</span>
        ) : null}
      </div>
      <div class="mt-1.5 text-[0.84rem] leading-[1.33] text-[#def0eb]/72">{props.help}</div>
    </div>
  );
}
