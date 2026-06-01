/**
 * Mirrors the backend ExHentaiEnqueueDownloadActivity.Config record.
 */
export interface ExHentaiEnqueueDownloadConfig {
  /** Per-task interval (ms). Backend rejects < 1000 (ExHentai rate limit). */
  intervalMs: number;
  autoRetry: boolean;
}
