import { useEffect, useRef, useState } from "react";
import type { Throughput } from "./model";
import BApi from "@/sdk/BApi";

export default function useAcquisitionThroughput(includeHistory: boolean) {
  const [snapshot, setSnapshot] = useState<Throughput>();
  const [disconnected, setDisconnected] = useState(false);
  const [delayed, setDelayed] = useState(false);
  const lastSample = useRef<{ stamp: string; receivedAt: number }>();
  const [receivedHistory, setReceivedHistory] = useState(false);

  useEffect(() => {
    let disposed = false;
    let pending = false;
    let next: ReturnType<typeof setTimeout>;
    let controller: AbortController;
    let interval = 5000;
    setReceivedHistory(false);

    async function refresh() {
      if (disposed || document.hidden || pending) return;
      pending = true;
      controller = new AbortController();
      const deadline = setTimeout(() => controller.abort(), 10000);
      try {
        const response = await BApi.acquisition.getAcquisitionThroughput(
          { includeHistory },
          {
            signal: controller.signal,
            showErrorToast: false,
            sensitiveResponse: true,
          },
        );
        if (disposed) return;
        if (response.code || !response.data || controller.signal.aborted)
          throw new Error("THROUGHPUT_UNAVAILABLE");
        interval = Math.max(1000, (response.data.intervalSeconds || 5) * 1000);
        const stamp = response.data.latest?.sampledAt;
        const now = performance.now();
        if (!stamp) lastSample.current = undefined;
        else if (lastSample.current?.stamp !== stamp)
          lastSample.current = { stamp, receivedAt: now };
        setDelayed(
          Boolean(lastSample.current && now - lastSample.current.receivedAt >= interval * 3),
        );
        setSnapshot(response.data);
        setReceivedHistory(includeHistory);
        setDisconnected(false);
      } catch {
        if (!disposed) setDisconnected(true);
      } finally {
        clearTimeout(deadline);
        pending = false;
        if (!disposed && !document.hidden) next = setTimeout(() => void refresh(), interval);
      }
    }

    function visibilityChanged() {
      clearTimeout(next);
      if (!document.hidden) void refresh();
    }

    void refresh();
    document.addEventListener("visibilitychange", visibilityChanged);
    return () => {
      disposed = true;
      clearTimeout(next);
      controller?.abort();
      document.removeEventListener("visibilitychange", visibilityChanged);
    };
  }, [includeHistory]);

  return { snapshot, disconnected, delayed, receivedHistory };
}
