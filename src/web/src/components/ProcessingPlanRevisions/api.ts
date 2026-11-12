import BApi from "@/sdk/BApi";
import type { SourceRef } from "@/components/Acquire/sourceHistory";

export async function querySourceProcessingPlan(source: SourceRef) {
  const response = await BApi.processingPlans.getSourceProcessingPlan(source, {
    sensitiveResponse: true,
    showErrorToast: false,
  });
  if (response.code || !response.data) throw new Error("PROCESSING_PLAN_QUERY_FAILED");
  return response.data;
}

export type SourceProcessingPlanView = Awaited<ReturnType<typeof querySourceProcessingPlan>>;
