export const LOG_LEVEL_OPTIONS = [
  { value: 0, label: 'Trace' },
  { value: 1, label: 'Debug' },
  { value: 2, label: 'Information' },
  { value: 3, label: 'Warning' },
  { value: 4, label: 'Error' },
  { value: 5, label: 'Critical' },
  { value: 6, label: 'None' }
];

export function getLogLevelLabel(value: number): string {
  const match = LOG_LEVEL_OPTIONS.find((option) => option.value === value);
  return match?.label ?? 'Warning';
}
