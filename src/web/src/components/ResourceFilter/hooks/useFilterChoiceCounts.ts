import type { ReferenceProperty } from "@/components/Property/referenceValues";
import type { SearchForm } from "@/pages/resource/models";

import { useEffect, useMemo, useState } from "react";
import { useResourceOwnerKey } from "@/features/management/resourceContext";

import { createChoiceResourceCountsStore } from "./choiceResourceCountsStore";

import {
  referenceValueCountsCriteria,
  useReferenceValueResourceCounts,
} from "@/hooks/useReferenceValueResourceCounts";

/** Resource-count display and availability policies belong to the filter using the options. */
export function useFilterChoiceCounts(property: ReferenceProperty, search?: SearchForm) {
  const ownerKey = useResourceOwnerKey();
  const globalUsage = useReferenceValueResourceCounts(property);
  const hasCriteria =
    !!search?.owner ||
    search?.deviceIds != null ||
    JSON.stringify(referenceValueCountsCriteria(search)) !== "{}";
  const filteredUsage = useReferenceValueResourceCounts(hasCriteria ? property : undefined, search);
  const usage = hasCriteria ? filteredUsage : globalUsage;
  const propertyKey = `${ownerKey}:${property.pool}:${property.id}:${property.type}`;
  const [retained, setRetained] = useState<{
    key: string;
    counts?: Record<string, number>;
  }>();
  const previous = retained?.key === propertyKey ? retained : undefined;
  // When clearing/reapplying criteria, use the last displayed result rather than an older
  // filtered hook result. A different property never inherits these counts.
  const counts = usage.counts && !usage.stale ? usage.counts : (previous?.counts ?? usage.counts);

  useEffect(() => {
    if (retained?.key !== propertyKey || retained.counts !== counts) {
      setRetained({ key: propertyKey, counts });
    }
  }, [propertyKey, counts, retained]);

  const state = useMemo(
    () => ({
      counts,
      loading: usage.loading,
      error: usage.error,
      stale: counts !== undefined && (usage.stale || counts !== usage.counts),
    }),
    [counts, usage.loading, usage.error, usage.stale, usage.counts],
  );
  const store = useMemo(createChoiceResourceCountsStore, [propertyKey]);

  useEffect(() => store.publish(state), [store, state]);

  // Current-filter zero counts can still be valid replacements or OR alternatives.
  const disabledKeys = useMemo(
    () =>
      globalUsage.counts === undefined
        ? undefined
        : new Set(Object.keys(globalUsage.counts).filter((id) => globalUsage.counts![id] === 0)),
    [globalUsage.counts],
  );

  return { store, disabledKeys, propertyKey, hasCounts: counts !== undefined };
}
