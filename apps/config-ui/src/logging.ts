const PREFIX = '[Jellycheckr][Admin]';

let verboseEnabled = false;

export function setAdminVerboseLogging(enabled: boolean): void {
  const next = Boolean(enabled);
  const changed = verboseEnabled !== next;
  verboseEnabled = next;

  if (changed && next) {
    write('debug', 'Verbose admin logging enabled');
  }
}

export function adminDebug(...args: unknown[]): void {
  if (!verboseEnabled) return;
  write('debug', ...args);
}

export function adminWarn(...args: unknown[]): void {
  write('warn', ...args);
}

function write(level: 'debug' | 'warn', ...args: unknown[]): void {
  if (level === 'warn') {
    console.warn(PREFIX, ...args);
    return;
  }

  if (typeof console.debug === 'function') {
    console.debug(PREFIX, ...args);
    return;
  }

  console.log(PREFIX, ...args);
}
