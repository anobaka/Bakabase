import type React from "react";

/**
 * Per-trigger UI bundle. Keyed by the backend's trigger kind (e.g. "subscription.updated").
 *
 * - FilterForm: edits this trigger's filter payload during workflow create/edit.
 * - FilterSummary: short human-readable description of the filter for list rows.
 *
 * <TFilter> is the typed shape of TriggerFilterJson for THIS trigger. The registry
 * uses `any` outwards; consumers cast at the call site.
 */
export interface WorkflowTriggerUI<TFilter = unknown> {
  kind: string;
  /**
   * i18n key for the trigger's display name. The server ships an English fallback in its
   * descriptor; the editor prefers this key so non-English locales render correctly.
   */
  displayNameKey?: string;
  /** Default empty filter for new workflows of this kind. */
  defaultFilter: () => TFilter;
  /** Parse the backend's opaque TriggerFilterJson. Null/empty → defaultFilter(). */
  parseFilter: (json: string | null | undefined) => TFilter;
  /** Serialize the filter back to JSON; return null when the filter is "match all". */
  serializeFilter: (filter: TFilter) => string | null;
  isValid: (filter: TFilter) => boolean;
  /**
   * Item type this trigger emits given the filter — mirror of the backend
   * IWorkflowTrigger.ResolveOutputItemType. Seeds the editor's chain walk.
   */
  resolveOutputItemType: (filter: TFilter) => string;
  FilterForm: React.FC<{ value: TFilter; onChange: (v: TFilter) => void }>;
  FilterSummary: React.FC<{ filter: TFilter }>;
}
