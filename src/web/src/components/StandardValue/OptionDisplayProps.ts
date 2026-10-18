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
  /** Opt-in display ordering. Reordering must never change the selected value. */
  optionOrdering?: {
    toolbar?: ReactNode;
    disabled?: boolean;
    failureLabel?: string;
    source?: {
      subscribe: (changed: () => void) => () => void;
      getSnapshot: () => readonly string[];
    };
    dragLabel: (label: string) => string;
    onMove: (active: string, over: string, visible: string[]) => Promise<boolean>;
  };
};
