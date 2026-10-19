import { useEffect, useState } from "react";

import BApi from "@/sdk/BApi";
import { DownloadTaskStatus } from "@/sdk/constants";

export type DownloadTaskExecution = NonNullable<
  Awaited<ReturnType<typeof BApi.downloadTask.getDownloadTaskExecutions>>["data"]
>[number];

export function executionDisplayStatus(
  sourceStatus: DownloadTaskStatus,
  execution?: DownloadTaskExecution,
): DownloadTaskStatus {
  switch (execution?.status) {
    case "failed":
    case "processingFailed":
      return DownloadTaskStatus.Failed;
    case "completed":
      return DownloadTaskStatus.Complete;
    case "running":
    case "processing":
      return DownloadTaskStatus.Downloading;
    case "queued":
    case "preparing":
      return DownloadTaskStatus.InQueue;
    case "waiting":
    case "stopped":
      return DownloadTaskStatus.Idle;
    default:
      return sourceStatus;
  }
}

const batchSize = 500;

/** Bounded batches form one snapshot; task progress pushes do not restart the polling loop. */
export default function useDownloadTaskExecutions(ids: number[]) {
  const key = ids.join(",");
  const [executions, setExecutions] = useState<Map<number, DownloadTaskExecution>>(() => new Map());

  useEffect(() => {
    let active = true;
    let loading = false;
    const currentIds = key ? key.split(",").map(Number) : [];
    const load = async () => {
      if (loading || currentIds.length === 0) return;
      loading = true;
      try {
        const snapshot = new Map<number, DownloadTaskExecution>();
        for (let offset = 0; offset < currentIds.length; offset += batchSize) {
          if (!active) return;
          const response = await BApi.downloadTask.getDownloadTaskExecutions({
            ids: currentIds.slice(offset, offset + batchSize),
          });
          if (!active || response.code) return;
          for (const item of response.data ?? []) {
            snapshot.set(item.downloadTaskId, item);
          }
        }
        setExecutions(snapshot);
      } catch {
        // Keep the last confirmed state until the next refresh; API reports request failures.
      } finally {
        loading = false;
      }
    };
    setExecutions(new Map());
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 3000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [key]);

  return executions;
}
