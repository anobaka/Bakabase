import { useEffect, useState } from "react";
import BApi from "@/sdk/BApi";

/** Reads a default only; per-execution choices never write acquisition settings. */
export default function useStorageReserveDefaults(enabled: boolean) {
  const [value, setValue] = useState<{ selected: boolean; minimumBytes: number }>();
  const [failed, setFailed] = useState(false);
  const [revision, reload] = useState(0);
  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    setValue(undefined);
    setFailed(false);
    void BApi.acquisition.getAcquisitionOptions({ signal: controller.signal, showErrorToast: false }).then(result => {
      const next = result.data;
      if (result.code || typeof next?.useEmergencySpaceReserve !== "boolean" ||
        typeof next.minimumFreeSpaceBytes !== "number" || !Number.isFinite(next.minimumFreeSpaceBytes))
        throw new Error("ACQUISITION_OPTIONS_UNAVAILABLE");
      if (!controller.signal.aborted) setValue({ selected: next.useEmergencySpaceReserve,
        minimumBytes: next.minimumFreeSpaceBytes });
    }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [enabled, revision]);
  return { value, failed, reload: () => reload(current => current + 1) };
}
