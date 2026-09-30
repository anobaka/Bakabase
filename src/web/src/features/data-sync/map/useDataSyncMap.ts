import type { DataSyncMapView } from "../api";

import { useCallback, useEffect, useRef, useState } from "react";

import { dataSyncApi } from "../api";
import { IDLE_POLL_MS, LIVE_POLL_MS } from "../hooks/useDataSyncPageData";
import { useDataSyncStore } from "../stores/dataSync";

import { isSyncMapLive } from "./mapAdapter";

const asError = (cause: unknown) => (cause instanceof Error ? cause : new Error(String(cause)));

/**
 * Data sync as the device map draws it (`GET /data-sync/map`), kept current on the map's own
 * schedule: every 5 s while a request to read this device's definitions, or this device's own
 * request, waits for an answer, every 15 s otherwise — only while the page is visible. Like the
 * map's other sources it stands alone: a read that fails leaves the others on the map, and a
 * quiet re-read never replaces a good answer with an error.
 */
export function useDataSyncMap() {
  const [view, setView] = useState<DataSyncMapView>();
  const [error, setError] = useState<Error>();
  const generation = useRef(0);
  const hasView = useRef(false);
  const loadOverview = useDataSyncStore((state) => state.load);

  /**
   * Reads it again. `quiet`: a timer's re-read. `withOverview`: after an action, this device's
   * own side (its sharing switch, its name, its counts) is read again too, for the details.
   */
  const load = useCallback(
    async (options: { quiet?: boolean; withOverview?: boolean } = {}) => {
      const run = ++generation.current;

      try {
        const [fresh] = await Promise.all([
          dataSyncApi.map(),
          options.withOverview ? loadOverview() : undefined,
        ]);

        if (run !== generation.current) return;
        if (fresh) {
          hasView.current = true;
          setView(fresh);
          setError(undefined);
        }
      } catch (cause) {
        if (run === generation.current && (!options.quiet || !hasView.current))
          setError(asError(cause));
      }
    },
    [loadOverview],
  );

  useEffect(() => {
    void load({ withOverview: true });

    return () => {
      generation.current += 1;
    };
  }, [load]);

  const live = isSyncMapLive(view);

  useEffect(() => {
    const timer = setInterval(
      () => {
        if (!document.hidden) void load({ quiet: true });
      },
      live ? LIVE_POLL_MS : IDLE_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, load]);

  return { view, error, load };
}
