export type CoverOperationsOptions = {
  displayOperations?: string[] | null;
  showAggregateOperations?: boolean | null;
};

export type CoverOperationsSelection = {
  displayOperations: string[];
  showAggregateOperations: boolean;
};

/** Preserve legacy aggregation until a user explicitly edits the new controls. */
export function resolveCoverOperations(options?: CoverOperationsOptions) {
  const previous = options?.displayOperations;

  return {
    showAggregate:
      options?.showAggregateOperations ?? (!previous?.length || previous.includes("aggregate")),
    directOperations: [...new Set(previous?.filter((key) => key !== "aggregate") ?? [])],
  };
}

export function updateCoverShortcut(
  options: CoverOperationsOptions | undefined,
  key: string,
  selected: boolean,
): CoverOperationsSelection {
  const { showAggregate, directOperations } = resolveCoverOperations(options);

  return {
    // Removing the old sentinel, or the last shortcut, must not change aggregation.
    showAggregateOperations: showAggregate,
    displayOperations: selected
      ? [...directOperations.filter((item) => item !== key), key]
      : directOperations.filter((item) => item !== key),
  };
}
