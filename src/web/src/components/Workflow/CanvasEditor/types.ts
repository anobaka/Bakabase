import type { WorkflowActivityErrorBehavior } from "@/sdk/constants";

/** One activity being edited. `clientId` is a stable React/drag key, never persisted. */
export interface ActivityDraft {
  clientId: string;
  kind: string;
  configJson: string;
  onItemError: WorkflowActivityErrorBehavior;
}

/** What the inspector is showing: the trigger node, or the activity at an index. */
export type CanvasSelection = "trigger" | number;

/** How a dragged kind fits into a slot: directly, via an auto-inserted AI bridge, or not. */
export type SlotFit = "direct" | "bridge" | null;

/** A resolved prefill for a fresh definition (template or page hand-off). */
export interface EditorSeedLike {
  name?: string;
  /** i18n key used when no literal name is given. */
  nameKey?: string;
  triggerKind: string;
  drafts: ActivityDraft[];
}
