import { useContext, useEffect, useState } from "react";
import BApi from "@/sdk/BApi";
import type { BakabaseServiceComponentsCloudDownloadsCloudDownloadResumeTarget as ResumeTarget } from "@/sdk/Api";
import { ResumeSubmissionContext } from "../../ResumeSubmissionContext";
import type { CloudTarget } from ".";

export function originalDownloadId(target: CloudTarget) {
  return target.transportTaskId || (target.downloadTaskId != null ? `site:${target.downloadTaskId}` : undefined);
}

/** Acquisitions already have a live preview. Standalone workflow forms only read their own bound downloads. */
export function useDownloadResumeTargets(runId: number | undefined, targets: readonly CloudTarget[], enabled: boolean) {
  const owner = useContext(ResumeSubmissionContext);
  const hasPreview = owner != null && owner.runId === runId;
  const [snapshot, setSnapshot] = useState<{ runId: number; targets: readonly CloudTarget[]; rows: ResumeTarget[] }>();
  useEffect(() => {
    if (hasPreview || !enabled || runId == null) return;
    const controller = new AbortController();
    const read = async (target: CloudTarget): Promise<ResumeTarget> => {
      const downloadId = originalDownloadId(target) ?? "";
      const row = { targetId: target.id, downloadId, status: "unavailable", canRetry: false };
      if (target.downloadArtifactId || target.processedArtifactId || target.publishedArtifactId)
        return { ...row, status: "completed" };
      if (target.excluded || !downloadId) return row;
      try {
        const response = await BApi.downloadCenter.getDownloadCenterTask(downloadId, {
          showErrorToast: false, sensitiveResponse: true, signal: controller.signal,
        });
        const task = response.data;
        if (!response.code && task?.id === downloadId && task.workflowRunId === runId)
          return { ...row, status: task.status, problem: task.error, canRetry: ["failed", "stopped"].includes(task.status) };
      } catch { /* A missing or unavailable original remains unselectable. */ }
      return row;
    };
    void (async () => {
      const rows: ResumeTarget[] = [];
      for (let start = 0; start < targets.length && !controller.signal.aborted; start += 8)
        rows.push(...await Promise.all(targets.slice(start, start + 8).map(read)));
      if (!controller.signal.aborted) setSnapshot({ runId, targets, rows });
    })();
    return () => controller.abort();
  }, [enabled, hasPreview, runId, targets]);
  if (hasPreview) return owner.downloadTargets;
  if (snapshot?.runId === runId && snapshot?.targets === targets) return snapshot?.rows;
}
