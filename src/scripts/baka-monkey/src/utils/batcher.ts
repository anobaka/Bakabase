/**
 * Generic request batcher.
 * Collects individual items within a short time window, then fires a single
 * batch request and resolves each caller's promise with its own result.
 *
 * Usage:
 *   const batcher = createBatcher<string, number>({
 *     delay: 50,
 *     execute: async (keys) => { ... return Map<string, number> },
 *   });
 *   const result = await batcher.enqueue('key1'); // batched with others
 */

interface BatcherOptions<K, V> {
  /** Milliseconds to wait before flushing the queue (default: 50). */
  delay?: number;
  /** Execute the batch request. Return a Map from key to result. */
  execute: (keys: K[]) => Promise<Map<K, V>>;
}

interface PendingEntry<K, V> {
  key: K;
  resolve: (value: V | undefined) => void;
  reject: (error: unknown) => void;
}

export function createBatcher<K, V>(options: BatcherOptions<K, V>) {
  const delay = options.delay ?? 50;
  let queue: PendingEntry<K, V>[] = [];
  let timer: ReturnType<typeof setTimeout> | null = null;

  function flush() {
    timer = null;
    const batch = queue;
    queue = [];

    const uniqueKeys = [...new Set(batch.map((e) => e.key))];

    options
      .execute(uniqueKeys)
      .then((resultMap) => {
        for (const entry of batch) {
          entry.resolve(resultMap.get(entry.key));
        }
      })
      .catch((err) => {
        for (const entry of batch) {
          entry.reject(err);
        }
      });
  }

  return {
    enqueue(key: K): Promise<V | undefined> {
      return new Promise<V | undefined>((resolve, reject) => {
        queue.push({ key, resolve, reject });
        if (!timer) {
          timer = setTimeout(flush, delay);
        }
      });
    },
  };
}
