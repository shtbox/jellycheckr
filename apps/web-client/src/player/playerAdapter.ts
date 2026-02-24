export interface MediaItem {
  id?: string;
  type?: string;
  seriesId?: string;
}

export interface PlayerAdapter {
  getSessionId(): string;
  getCurrentItem(): MediaItem | null;
  stopPlayback(): void;
  exitPlaybackView?(): void;
  on(eventName: string, handler: (...args: unknown[]) => void): () => void;
}
