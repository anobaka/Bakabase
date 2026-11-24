export const acquisitionFactFilters = ["downloadedPending", "spaceInsufficient"] as const;

/** A received facet dictionary is complete; a missing response has no counts yet. */
export function acquisitionFilterCount(counts: Record<string, number> | null | undefined, id: string) {
  return counts == null ? undefined : counts[id] ?? 0;
}

/** Unknown counts may label the current selection, but cannot establish a new filter choice. */
export function showAcquisitionFilter(count: number | undefined, selectedOrAll: boolean) {
  return selectedOrAll || (count !== undefined && count > 0);
}

export type AcquisitionFilterSelection = {
  attentionTags: string[];
  facts: string[];
};

type FilterInput = {
  attentionTag?: string | null;
  attentionTags?: readonly string[] | null;
  facts?: readonly string[] | null;
};

/** Equivalent AND sets share a query key, regardless of click order. */
export function normalizeAcquisitionFilters(input: FilterInput): AcquisitionFilterSelection {
  const canonical = (values: readonly string[]) => [...new Set(values)].sort();
  return {
    // The retired catch-all has no equivalent AND filter after splitting its causes.
    // Discard saved selections so an older browser preference cannot reject the search.
    attentionTags: canonical([
      ...(input.attentionTags ?? []),
      ...(input.attentionTag ? [input.attentionTag] : []),
    ].filter(tag => tag !== "planNeedsInput")),
    facts: canonical(input.facts ?? []),
  };
}

export function toggleAcquisitionFilter(values: readonly string[], value: string): string[] {
  return [...new Set(values.includes(value) ? values.filter(item => item !== value) : [...values, value])].sort();
}
