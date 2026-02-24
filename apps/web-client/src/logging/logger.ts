const PREFIX = "[Jellycheckr]";
const MAX_BUFFERED_DEBUG_LOGS = 500;

let debugEnabled = false;
const bufferedDebugLogs: unknown[][] = [];

export function setDebugLogging(enabled: boolean): void {
  const nextEnabled = Boolean(enabled);
  const changed = debugEnabled !== nextEnabled;
  debugEnabled = nextEnabled;

  if (changed && nextEnabled) {
    write("debug", "Debug logging enabled");
    flushBufferedDebugLogs();
  }
}

export function debug(...args: unknown[]): void {
  if (!debugEnabled) {
    bufferDebugLog(args);
    return;
  }

  write("debug", ...args);
}

export function warn(...args: unknown[]): void {
  write("warn", ...args);
}

export function error(...args: unknown[]): void {
  write("error", ...args);
}

function write(level: "debug" | "warn" | "error", ...args: unknown[]): void {
  if (level === "warn") {
    console.warn(PREFIX, ...args);
    return;
  }

  if (level === "error") {
    console.error(PREFIX, ...args);
    return;
  }

  if (typeof console.debug === "function") {
    console.debug(PREFIX, ...args);
    return;
  }

  console.log(PREFIX, ...args);
}

function bufferDebugLog(args: unknown[]): void {
  bufferedDebugLogs.push(args);
  if (bufferedDebugLogs.length > MAX_BUFFERED_DEBUG_LOGS) {
    bufferedDebugLogs.shift();
  }
}

function flushBufferedDebugLogs(): void {
  if (bufferedDebugLogs.length === 0) {
    return;
  }

  const count = bufferedDebugLogs.length;
  write("debug", `Flushing ${count} buffered debug log(s) captured before debug logging was enabled`);
  while (bufferedDebugLogs.length > 0) {
    const entry = bufferedDebugLogs.shift();
    if (entry) {
      write("debug", "[buffered]", ...entry);
    }
  }
}
