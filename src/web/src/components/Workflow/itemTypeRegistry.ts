import type { components } from "@/sdk/BApi2";

export type WorkflowItemTypeDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowItemTypeDescriptorViewModel"];

/**
 * Tiny indexer over the server-side item type descriptors. Used by:
 * - The chain "type pill" (display name + field list popover).
 * - The AI transform's ConfigForm (target type Select).
 *
 * Fetched once per editor mount; we don't reload on save since types are static.
 */
export class WorkflowItemTypeIndex {
  private readonly byType: Map<string, WorkflowItemTypeDescriptorVm>;

  constructor(descriptors: WorkflowItemTypeDescriptorVm[]) {
    this.byType = new Map(descriptors.map((d) => [d.itemType, d]));
  }

  get(itemType: string): WorkflowItemTypeDescriptorVm | undefined {
    return this.byType.get(itemType);
  }

  all(): WorkflowItemTypeDescriptorVm[] {
    return Array.from(this.byType.values());
  }

  displayName(itemType: string): string {
    return this.byType.get(itemType)?.displayName ?? itemType;
  }
}
