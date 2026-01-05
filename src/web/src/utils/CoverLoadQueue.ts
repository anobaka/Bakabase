/**
 * Queue manager for controlling concurrent cover image loading.
 * Prevents browser connection saturation by limiting concurrent requests.
 */
export class CoverLoadQueue {
  private concurrency: number;
  private running: number = 0;
  private queue: Array<{
    url: string;
    signal: AbortSignal;
    resolve: (value: string) => void;
    reject: (error: Error) => void;
    onAbort: () => void;
  }> = [];

  /**
   * @param concurrency Maximum number of concurrent cover loads (default: 6)
   */
  constructor(concurrency: number = 6) {
    this.concurrency = concurrency;
  }

  /**
   * Add a cover load task to the queue with cancellation support.
   * @param url The cover image URL to load
   * @param signal AbortSignal to allow request cancellation
   * @returns Promise that resolves to blob URL or rejects on error
   */
  async loadCover(url: string, signal: AbortSignal): Promise<string> {
    return new Promise<string>((resolve, reject) => {
      // Check if already aborted
      if (signal.aborted) {
        reject(new DOMException("Aborted", "AbortError"));
        return;
      }

      // Handle abort - remove from queue if still pending
      const onAbort = () => {
        const index = this.queue.findIndex((t) => t.onAbort === onAbort);
        if (index !== -1) {
          this.queue.splice(index, 1);
          reject(new DOMException("Aborted", "AbortError"));
        }
        // If not in queue, the task is already running - fetch will handle abort
      };

      const task = { url, signal, resolve, reject, onAbort };

      signal.addEventListener("abort", onAbort, { once: true });

      // Add to queue
      this.queue.push(task);

      // Try to process
      this.processNext();
    });
  }

  private async processNext(): Promise<void> {
    // Check if we can start a new task
    if (this.running >= this.concurrency || this.queue.length === 0) {
      return;
    }

    // Find first non-aborted task
    let task = this.queue.shift();
    while (task && task.signal.aborted) {
      // Clean up abort listener for skipped tasks
      task.signal.removeEventListener("abort", task.onAbort);
      task = this.queue.shift();
    }

    if (!task) {
      return;
    }

    // Increment running count BEFORE any async operation
    this.running++;

    const { url, signal, resolve, reject, onAbort } = task;

    try {
      // Check if aborted before starting fetch
      if (signal.aborted) {
        reject(new DOMException("Aborted", "AbortError"));
        return;
      }

      const response = await fetch(url, { signal });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      const blob = await response.blob();
      const blobUrl = URL.createObjectURL(blob);
      resolve(blobUrl);
    } catch (error) {
      reject(error as Error);
    } finally {
      // Clean up abort listener
      signal.removeEventListener("abort", onAbort);
      // Always decrement running count and try next task
      this.running--;
      this.processNext();
    }
  }

  /**
   * Get current queue length (pending tasks)
   */
  get length(): number {
    return this.queue.length;
  }

  /**
   * Get number of tasks currently being processed
   */
  get pending(): number {
    return this.running;
  }

  /**
   * Clear all pending tasks from the queue
   */
  clear(): void {
    // Reject all pending tasks and clean up listeners
    while (this.queue.length > 0) {
      const task = this.queue.shift();
      if (task) {
        task.signal.removeEventListener("abort", task.onAbort);
        task.reject(new Error("Queue cleared"));
      }
    }
  }

  /**
   * Stop the queue and clear all tasks
   */
  stop(): void {
    this.clear();
  }
}

export default CoverLoadQueue;
