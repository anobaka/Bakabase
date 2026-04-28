import { useMemo, useState } from "react";

import {
  Filter,
  FilterProvider,
  createDefaultFilterConfig,
  type SearchFilter,
} from "@/components/ResourceFilter";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { ResourceMatcherLeaf } from "@/pages/health-score/types";

interface Props {
  leaf: ResourceMatcherLeaf;
  onChange: (next: ResourceMatcherLeaf) => void;
}

/**
 * Single-filter editor for a Property-kind <see cref="ResourceMatcherLeaf"/>.
 * Wraps the existing <c>Filter</c> component (which expects a FilterProvider
 * context). The wrapper keeps the full <c>SearchFilter</c> shape locally so
 * the property/valueProperty/availableOperations fetched by Filter survive
 * re-renders — the leaf only carries the wire-level subset.
 *
 * Internal disable/delete buttons are hidden because LeafRow already renders
 * its own delete (and the leaf's <c>negated</c> switch is conceptually
 * different from a disabled flag).
 */
export const PropertyLeafEditor = ({ leaf, onChange }: Props) => {
  const { createPortal } = useBakabaseContext();
  const config = useMemo(() => createDefaultFilterConfig(createPortal), [createPortal]);

  const [filterState, setFilterState] = useState<SearchFilter>(() => ({
    propertyPool: leaf.propertyPool ?? undefined,
    propertyId: leaf.propertyId ?? undefined,
    operation: leaf.operation ?? undefined,
    dbValue: leaf.propertyValue ?? undefined,
    disabled: leaf.disabled ?? false,
  } as SearchFilter));

  const handleChange = (next: SearchFilter) => {
    setFilterState(next);
    onChange({
      ...leaf,
      propertyPool: next.propertyPool,
      propertyId: next.propertyId,
      operation: next.operation,
      propertyValue: next.dbValue,
    });
  };

  return (
    <FilterProvider config={config}>
      <Filter
        filter={filterState}
        layout="horizontal"
        removeBackground
        hideInternalActions
        autoTriggerPropertySelector={!leaf.propertyId}
        onChange={handleChange}
      />
    </FilterProvider>
  );
};
