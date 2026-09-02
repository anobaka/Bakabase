import type { components } from "@/sdk/BApi2";
import type { WorkflowItemTypeIndex } from "./itemTypeRegistry";

import { WorkflowItemTypeBehavior } from "@/sdk/constants";
import { getWorkflowActivityUI } from "./Activities";

type ActivityDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowActivityDescriptorViewModel"];

export interface ChainStep {
  /** Item type entering this position. */
  typeBefore: string;
  /** Item type leaving this position (= input for the next step). */
  typeAfter: string;
  /** Whether the activity at this position accepts typeBefore. */
  compatible: boolean;
}

export interface ChainWalkResult {
  steps: ChainStep[];
  /** Item type after the whole chain — used to filter the "add activity" picker. */
  typeAfter: string;
  /** True if every activity in the chain is compatible. */
  allCompatible: boolean;
}

/**
 * Mirror of the backend's accept rule (WorkflowDefinitionService.ValidateActivities). The
 * algorithm is duplicated on purpose — it's tiny and stable — but the facts are not: tags and
 * contracts both come from the backend descriptors, so this stays a generic set operation
 * (capability map §9·决定 4).
 */
function accepts(
  descriptor: ActivityDescriptorVm | undefined,
  currentType: string,
  itemTypes: WorkflowItemTypeIndex,
): boolean {
  if (!descriptor) return false;
  const accepted = descriptor.acceptedInputItemTypes ?? [];
  const contract = descriptor.acceptedItemInterface ?? null;
  // Both surfaces empty = accepts any concrete type (including the heterogeneous "any" tag).
  if (accepted.length === 0 && !contract) return true;
  if (accepted.includes(currentType)) return true;
  return !!contract && itemTypes.implementsInterfaces(currentType).includes(contract);
}

/**
 * Walk the activity chain from the trigger's output type, mirroring the backend
 * WorkflowDefinitionService.ValidateActivities. Returns per-position compatibility plus
 * the resulting type so the picker can offer only addable activities, and so the editor
 * can render an "item type" pill between cards.
 */
export function walkChain(
  startType: string,
  activities: Array<{ kind: string; configJson: string }>,
  descriptorByKind: Map<string, ActivityDescriptorVm>,
  itemTypes: WorkflowItemTypeIndex,
): ChainWalkResult {
  const steps: ChainStep[] = [];
  let currentType = startType;
  let allCompatible = true;

  for (let i = 0; i < activities.length; i++) {
    const { kind, configJson } = activities[i];
    const descriptor = descriptorByKind.get(kind);
    const typeBefore = currentType;
    const compatible = accepts(descriptor, currentType, itemTypes);
    if (!compatible) allCompatible = false;

    let typeAfter = currentType;
    if (descriptor) {
      switch (descriptor.outputBehavior) {
        case WorkflowItemTypeBehavior.Passthrough:
          typeAfter = currentType;
          break;
        case WorkflowItemTypeBehavior.Fixed:
          typeAfter = descriptor.fixedOutputItemType ?? currentType;
          break;
        case WorkflowItemTypeBehavior.AdaptToNext: {
          // Mirror the backend: ask the activity's UI to resolve from config; fall back to
          // the next step's single accepted type. If nothing resolves, mark incompatible
          // (the editor's red border surfaces it).
          const nextHint = peekNextSingleAcceptedType(i, activities, descriptorByKind);
          const ui = getWorkflowActivityUI(kind);
          const resolved =
            (ui?.resolveAdaptedOutputType?.(configJson, nextHint) ?? nextHint) ?? null;
          if (resolved == null) {
            allCompatible = false;
            typeAfter = currentType;  // pretend passthrough so downstream walk is sane
          } else {
            typeAfter = resolved;
          }
          break;
        }
      }
    }

    steps.push({ typeBefore, typeAfter, compatible });
    currentType = typeAfter;
  }

  return { steps, typeAfter: currentType, allCompatible };
}

function peekNextSingleAcceptedType(
  index: number,
  activities: Array<{ kind: string }>,
  descriptorByKind: Map<string, ActivityDescriptorVm>,
): string | null {
  const next = activities[index + 1];
  if (!next) return null;
  const d = descriptorByKind.get(next.kind);
  if (!d) return null;
  const accepted = d.acceptedInputItemTypes ?? [];
  return accepted.length === 1 ? accepted[0] : null;
}

export function descriptorAccepts(
  descriptor: ActivityDescriptorVm,
  currentType: string,
  itemTypes: WorkflowItemTypeIndex,
): boolean {
  return accepts(descriptor, currentType, itemTypes);
}
