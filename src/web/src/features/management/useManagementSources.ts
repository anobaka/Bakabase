import { useCallback, useEffect, useRef, useState } from "react";

import { getManagementSources, type ManagementSource } from "./api";

export function useManagementSources() {
  const [sources, setSources] = useState<ManagementSource[]>([]);
  const [error, setError] = useState<Error>();
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const initialized = useRef(false);
  const refresh = useCallback(() => setRevision((x) => x + 1), []);

  useEffect(() => {
    const controller = new AbortController();

    let timer: ReturnType<typeof setTimeout>;

    setLoading(!initialized.current);
    const load = async () => {
      let poll = true;

      try {
        const next = await getManagementSources(controller.signal);

        if (controller.signal.aborted) return;
        setSources((current) =>
          JSON.stringify(current) === JSON.stringify(next) ? current : next,
        );
        setError(undefined);
        poll = next.some((source) => !source.isLocal);
      } catch (reason) {
        if (!controller.signal.aborted)
          setError(reason instanceof Error ? reason : new Error(String(reason)));
      } finally {
        if (!controller.signal.aborted) {
          initialized.current = true;
          setLoading(false);
          if (poll) timer = setTimeout(tick, 5000);
        }
      }
    };
    const tick = () => {
      if (document.hidden) timer = setTimeout(tick, 5000);
      else void load();
    };

    void load();

    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [revision]);

  return { sources, error, loading, refresh };
}
