const FIELD_CARD_CLASS = 'rounded-xl border border-white/20 bg-white/[0.03] p-3';
const FIELD_INPUT_CLASS = 'w-full rounded-[10px] border border-white/25 bg-[#051012]/55 px-2.5 py-2 text-sm text-[#e8f3f1] outline-none transition focus:border-[#67d8bf]/75 focus:ring-2 focus:ring-[#67d8bf]/40';

export function TextField(props: {
  id: string;
  label: string;
  value: string;
  help: string;
  onInput: (ev: any) => void;
}) {
  return (
    <div class={FIELD_CARD_CLASS}>
      <div class="mb-1.5 flex items-start justify-between gap-2.5">
        <label class="text-sm font-bold leading-[1.22] text-[#e8f3f1]" for={props.id}>{props.label}</label>
      </div>
      <textarea
        id={props.id}
        rows={3}
        value={props.value}
        onInput={props.onInput}
        class={`${FIELD_INPUT_CLASS} min-h-[84px] resize-y`}
      />
      <div class="mt-1.5 text-[0.84rem] leading-[1.33] text-[#def0eb]/72">{props.help}</div>
    </div>
  );
}
