/**
 * Mirrors the backend DownloaderEnqueueActivity.Config record.
 */
export interface DownloaderEnqueueConfig {
  /** Per-task interval (ms). The slowest platform's floor, not a preference — 1000 is the minimum. */
  intervalMs: number;
  autoRetry: boolean;
}
