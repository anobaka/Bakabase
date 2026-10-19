import type { OptionDisplayProps } from "./OptionDisplayProps";

import { useMemo, useSyncExternalStore } from "react";

const EmptyOrder: readonly string[] = [];
const emptyOrder = () => EmptyOrder;
const noOrderSubscription = () => () => {};

/** Apply configuration changes to open editors without changing selection or focus. */
export function useOrderedChoices<T extends { value: string }>(
  choices: T[],
  source?: OptionDisplayProps["optionOrderSource"],
): T[] {
  const order = useSyncExternalStore(
    source?.subscribe ?? noOrderSubscription,
    source?.getSnapshot ?? emptyOrder,
  );

  return useMemo(() => {
    if (!order.length) return choices;
    const positions = new Map(order.map((key, index) => [key, index]));

    return [...choices].sort(
      (a, b) => (positions.get(a.value) ?? order.length) - (positions.get(b.value) ?? order.length),
    );
  }, [choices, order]);
}
