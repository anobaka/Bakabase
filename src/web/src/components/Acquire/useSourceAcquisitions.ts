import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import BApi from "@/sdk/BApi";
import { AcquisitionStatus } from "@/sdk/constants";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionTask as AcquisitionTask } from "@/sdk/Api";
import { sourceRefKey, type SourceRef } from "./sourceHistory";

/** Prepared acquisitions have no workflow run yet; query their business identity directly. */
export function useSourceAcquisitions(sources: SourceRef[], refreshKey = "") {
  const identity = JSON.stringify(sources);
  const stable = useMemo<SourceRef[]>(() => JSON.parse(identity), [identity]);
  const [rows, setRows] = useState<Map<string, AcquisitionTask[]>>(new Map());
  const [error, setError] = useState(false);
  const [revision, setRevision] = useState(0);
  const scope = useRef<string>();
  const refresh = useCallback(() => setRevision((value) => value + 1), []);
  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    if (scope.current !== identity) {
      scope.current = identity;
      setRows(new Map());
    }
    const load = async () => {
      try {
        const next = new Map<string, AcquisitionTask[]>();
        for (let offset = 0; offset < stable.length; offset += 100) {
          const response = await BApi.acquisition.getAcquisitionPlansBySources({
            sources: stable.slice(offset, offset + 100),
          });
          if (response.code) throw new Error(response.message ?? "Acquisition query failed");
          for (const result of response.data ?? [])
            next.set(sourceRefKey(result.source), result.tasks);
        }
        if (disposed) return;
        setRows(next);
        setError(false);
        if (
          [...next.values()].some((tasks) =>
            tasks.some((task) =>
              [
                AcquisitionStatus.Pending,
                AcquisitionStatus.Running,
                AcquisitionStatus.Waiting,
              ].includes(task.status),
            ),
          )
        )
          timer = setTimeout(() => void load(), 5000);
      } catch {
        if (!disposed) {
          setError(true);
          timer = setTimeout(() => void load(), 15000);
        }
      }
    };
    void load();
    return () => {
      disposed = true;
      if (timer) clearTimeout(timer);
    };
  }, [identity, stable, revision, refreshKey]);
  return { rows, error, refresh };
}
