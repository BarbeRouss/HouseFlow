/**
 * Centralized client-side error logger.
 *
 * All UI errors (error boundaries, unhandled rejections, Next.js error pages)
 * should flow through this module so we have a single place to hook in a
 * remote error-tracking service (Sentry, LogRocket, …) later.
 */

export interface ErrorLogEntry {
  message: string;
  source: "error-boundary" | "global-error" | "route-error" | "unhandled";
  componentStack?: string | null;
  digest?: string;
  url?: string;
  timestamp: string;
}

const MAX_LOG_SIZE = 50;
const STORAGE_KEY = "houseflow_error_log";

function getStoredLog(): ErrorLogEntry[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as ErrorLogEntry[]) : [];
  } catch {
    return [];
  }
}

function persistLog(entries: ErrorLogEntry[]) {
  if (typeof window === "undefined") return;
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(entries));
  } catch {
    // sessionStorage full – silently drop
  }
}

/**
 * Log a client-side error. Currently persists to sessionStorage and
 * console.error. Replace the body with a fetch() call when a remote
 * error-tracking endpoint is available.
 */
export function logClientError(
  error: unknown,
  source: ErrorLogEntry["source"],
  extra?: { componentStack?: string | null; digest?: string },
) {
  const entry: ErrorLogEntry = {
    message: error instanceof Error ? error.message : String(error),
    source,
    componentStack: extra?.componentStack ?? null,
    digest: extra?.digest,
    url: typeof window !== "undefined" ? window.location.href : undefined,
    timestamp: new Date().toISOString(),
  };

  // Console (always)
  console.error(`[${source}]`, error, extra);

  // Session-persistent ring buffer
  const log = getStoredLog();
  log.push(entry);
  if (log.length > MAX_LOG_SIZE) log.splice(0, log.length - MAX_LOG_SIZE);
  persistLog(log);
}

/**
 * Read the in-session error log (useful for debugging / support forms).
 */
export function getClientErrorLog(): ErrorLogEntry[] {
  return getStoredLog();
}
