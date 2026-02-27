import type { PluginConfig } from '../../types';

export type UpdateFieldHandler = <K extends keyof PluginConfig>(key: K, value: PluginConfig[K]) => void;

export type NumberHandlerFactory = <K extends keyof PluginConfig>(
  key: K,
  min: number,
  max: number
) => (ev: any) => void;
