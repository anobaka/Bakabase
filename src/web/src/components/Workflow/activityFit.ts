import type { components } from "@/sdk/BApi2";

type ActivityDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowActivityDescriptorViewModel"];

/**
 * Per-activity classification at the current chain tail:
 *  - "direct"  — current type is already accepted; add the activity as-is.
 *  - "bridge"  — not accepted, but the activity declares exactly one accepted type, so an
 *                AI transform can be auto-inserted in front of it (with its TargetItemType
 *                pinned to that single type) to bridge.
 *  - omitted   — incompatible and unbridgeable (activity accepts multiple types or none
 *                of them are reachable), so it doesn't show up in the picker.
 */
export type ActivityFitKind = "direct" | "bridge";

export interface DescriptorWithFit {
  descriptor: ActivityDescriptorVm;
  fit: ActivityFitKind;
  /** Pinned target type for the auto-inserted AI transform — only set when fit==="bridge". */
  bridgeTargetType?: string;
}

/**
 * Decide whether an activity can be added at the chain tail, and how (direct vs via an
 * auto-inserted AI bridge).
 */
export function classifyActivity(
  descriptor: ActivityDescriptorVm,
  currentType: string,
): DescriptorWithFit | null {
  const accepted = descriptor.acceptedInputItemTypes ?? [];
  // Empty accepted = accepts any concrete type, including the heterogeneous "any" tag.
  if (accepted.length === 0 || accepted.includes(currentType)) {
    return { descriptor, fit: "direct" };
  }
  // Single-accept activities can be bridged by an AI transform that emits exactly that type.
  if (accepted.length === 1) {
    return { descriptor, fit: "bridge", bridgeTargetType: accepted[0] };
  }
  // Multi-accept activities have no obvious bridge target; skip from the picker entirely.
  return null;
}
