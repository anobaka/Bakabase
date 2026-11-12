import type {
  BakabaseServiceComponentsAcquisitionHandoffsPostAcquisitionCatchUpItem as CatchUpItem,
  BakabaseServiceComponentsAcquisitionHandoffsPostAcquisitionCatchUpPreview as CatchUpPreview,
  BakabaseServiceComponentsAcquisitionHandoffsPostAcquisitionCatchUpResult as CatchUpResult,
  BakabaseServiceComponentsAcquisitionHandoffsPostAcquisitionCatchUpSelection as CatchUpSelection,
} from "@/sdk/Api";

export type { CatchUpItem, CatchUpPreview, CatchUpResult, CatchUpSelection };

export const catchUpSelectionKey = (selection: CatchUpSelection) =>
  JSON.stringify([
    selection.taskId,
    selection.revision,
    selection.workflowRunId ?? null,
    selection.resultsHash,
  ]);

export function hasValidCatchUpSelection(item: CatchUpItem): boolean {
  const selection = item.selection;
  return (
    !!selection &&
    selection.taskId === item.taskId &&
    Number.isSafeInteger(selection.revision) &&
    selection.revision >= 0 &&
    (selection.workflowRunId == null ||
      (Number.isSafeInteger(selection.workflowRunId) && selection.workflowRunId > 0)) &&
    /^[a-f0-9]{64}$/i.test(selection.resultsHash)
  );
}

/** A malformed or partial response must never silently widen or shrink approval. */
export function isValidCatchUpPreview(
  preview: CatchUpPreview | undefined,
  expectedTaskIds?: readonly number[],
): preview is CatchUpPreview {
  if (!preview || !Array.isArray(preview.items)) return false;
  const ids = preview.items.map((item) => item.taskId);
  return (
    preview.requestedCount === ids.length &&
    ids.every((id) => Number.isSafeInteger(id) && id > 0) &&
    new Set(ids).size === ids.length &&
    (!expectedTaskIds ||
      (ids.length === expectedTaskIds.length && expectedTaskIds.every((id) => ids.includes(id)))) &&
    preview.items.every(
      (item) =>
        ["eligible", "existing", "rejected"].includes(item.status) &&
        Array.isArray(item.acquisitionTaskIds) &&
        (item.status !== "eligible" || hasValidCatchUpSelection(item)),
    ) &&
    preview.eligibleCount === preview.items.filter((item) => item.status === "eligible").length &&
    preview.existingCount === preview.items.filter((item) => item.status === "existing").length &&
    preview.rejectedCount === preview.items.filter((item) => item.status === "rejected").length
  );
}

export function isValidCatchUpResult(
  result: CatchUpResult | undefined,
  submitted: readonly CatchUpSelection[],
): result is CatchUpResult {
  if (!result || !Array.isArray(result.items)) return false;
  const ids = result.items.map((item) => item.taskId);
  return (
    result.requestedCount === submitted.length &&
    ids.length === submitted.length &&
    new Set(ids).size === ids.length &&
    submitted.every((selection) => ids.includes(selection.taskId)) &&
    result.items.every((item) => ["accepted", "existing", "rejected"].includes(item.status)) &&
    result.acceptedCount === result.items.filter((item) => item.status === "accepted").length &&
    result.existingCount === result.items.filter((item) => item.status === "existing").length &&
    result.rejectedCount === result.items.filter((item) => item.status === "rejected").length
  );
}
