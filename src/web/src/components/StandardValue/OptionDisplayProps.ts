import type { ReactNode } from "react";

/** Additional presentation for selectable options without changing their labels or values. */
export type OptionDisplayProps<V = string> = {
  /**
   * Display-only content appended to an option; never used for searching or value conversion.
   * Return a component with its own subscription when content must update in an open editor.
   */
  renderOptionExtra?: (option: { value: V; label: string }) => ReactNode;
  /** Supporting content shown above the options in the full editor. */
  optionsDescription?: ReactNode;
  /** Read-only configured option order; editors cannot write or rearrange it. */
  optionOrderSource?: {
    subscribe: (changed: () => void) => () => void;
    getSnapshot: () => readonly string[];
  };
};
