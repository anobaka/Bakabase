import { useCallback, useEffect, useState } from "react";

import { getManagementSources, type ManagementSource } from "./api";

export function useManagementSources() {
  const [sources, setSources] = useState<ManagementSource[]>([]);
  const [error, setError] = useState<Error>();
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const refresh = useCallback(() => setRevision((x) => x + 1), []);

  useEffect(() => {
    const controller = new AbortController();

    setLoading(true);
    getManagementSources(controller.signal)
      .then((next) => {
        setSources(next);
        setError(undefined);
      })
      .catch((reason) => {
        if (!controller.signal.aborted) setError(reason);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, [revision]);

  return { sources, error, loading, refresh };
}
