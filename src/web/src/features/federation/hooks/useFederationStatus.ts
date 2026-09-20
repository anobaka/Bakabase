import type { FederationStatus } from "../types";

import { useCallback, useEffect, useRef, useState } from "react";

import { federationPeerApi } from "../peerApi";
import { isAbort } from "../transport";

/** Local status only: no remote hub or options are attached to the application stores. */
export function useFederationStatus() {
  const [status, setStatus] = useState<FederationStatus>();
  const [error, setError] = useState<Error>();
  const [loading, setLoading] = useState(false);
  const generation = useRef(0);
  const active = useRef<AbortController>();

  const refresh = useCallback(async () => {
    const run = ++generation.current;

    active.current?.abort();
    const controller = new AbortController();

    active.current = controller;
    setLoading(true);
    try {
      const fresh = await federationPeerApi.status(controller.signal);

      if (run === generation.current) {
        setStatus(fresh);
        setError(undefined);
      }
    } catch (cause) {
      if (run === generation.current && !isAbort(cause)) {
        setError(cause instanceof Error ? cause : new Error(String(cause)));
      }
    } finally {
      if (run === generation.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();

    return () => {
      generation.current += 1;
      active.current?.abort();
    };
  }, [refresh]);

  return { status, error, loading, refresh };
}
