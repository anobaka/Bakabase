import type { BakabaseServiceModelsViewDashboardOverviewViewModel } from "@/sdk/Api";

import { useEffect, useState } from "react";

import BApi from "@/sdk/BApi";

export function useDashboardOverview(refreshKey: number) {
  const [data, setData] = useState<BakabaseServiceModelsViewDashboardOverviewViewModel>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [updatedAt, setUpdatedAt] = useState<Date>();

  useEffect(() => {
    let cancelled = false;

    setLoading(true);
    setError(false);
    void BApi.dashboard
      .getDashboardOverview()
      .then((response) => {
        if (response.code || !response.data) throw new Error("Dashboard overview unavailable");
        if (cancelled) return;
        setData(response.data);
        setUpdatedAt(new Date());
      })
      .catch(() => {
        if (!cancelled) setError(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [refreshKey]);

  return { data, loading, error, updatedAt };
}
