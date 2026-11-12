import type {
  BakabaseModulesWorkflowAbstractionsModelsDomainWorkflowSourceRef,
  BakabaseServiceControllersWorkflowSourceRunView,
  BakabaseServiceControllersWorkflowSourceRunsView,
} from "@/sdk/Api";
import BApi from "@/sdk/BApi";

export type SourceRef = BakabaseModulesWorkflowAbstractionsModelsDomainWorkflowSourceRef;
export type SourceRun = BakabaseServiceControllersWorkflowSourceRunView;
export type SourceRuns = BakabaseServiceControllersWorkflowSourceRunsView;
export interface SourceHistoryFilter { excludedTriggerKinds?: string[]; }

export const sourceRefKey = (source: SourceRef) =>
  JSON.stringify([
    source.module,
    source.entityKind,
    source.entityId,
    source.revision ?? null,
    source.resultKey ?? null,
  ]);

export async function querySourceRuns(
  sources: SourceRef[],
  pageIndex = 1,
  pageSize = 5,
  filter: SourceHistoryFilter = {},
): Promise<SourceRuns[]> {
  const response = await BApi.workflowSources.queryWorkflowSourceRuns({
    sources,
    pageIndex,
    pageSize,
    ...filter,
  });
  if (response.code) throw new Error(response.message || "SOURCE_HISTORY_FAILED");
  return response.data ?? [];
}
