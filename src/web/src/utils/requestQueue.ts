import Queue from "queue";

// Limit concurrent resource-related requests to leave room for other operations
// Browser typically allows 6 concurrent requests per domain
// We limit resource requests to 4, leaving 2 for UI operations
const resourceQueue = new Queue({
  concurrency: 4,
  autostart: true,
});

export const ResourceRequestQueue = {
  push: <T>(fn: () => Promise<T>): Promise<T> => {
    return new Promise((resolve, reject) => {
      resourceQueue.push(async () => {
        try {
          const result = await fn();
          resolve(result);
        } catch (e) {
          reject(e);
        }
      });
    });
  },

  get pending() {
    return resourceQueue.length;
  },
};

export default ResourceRequestQueue;
