import type { WorkflowTriggerUI } from "./types";

import { SubscriptionUpdatedTriggerUI } from "./SubscriptionUpdated";
import { DownloaderCompletedTriggerUI } from "./DownloaderCompleted";
import { FsManualScanTriggerUI } from "./FsManualScan";
import { FsScheduledScanTriggerUI } from "./FsScheduledScan";
import { FsWatchTriggerUI } from "./FsWatch";

/**
 * Registry of trigger UIs keyed by their backend `kind`.
 * Triggers that the server reports but the frontend hasn't shipped a UI for
 * fall back to a read-only "raw JSON" display in the editor.
 */
export const workflowTriggerRegistry: Record<string, WorkflowTriggerUI<any>> = {
  [SubscriptionUpdatedTriggerUI.kind]: SubscriptionUpdatedTriggerUI,
  [DownloaderCompletedTriggerUI.kind]: DownloaderCompletedTriggerUI,
  [FsManualScanTriggerUI.kind]: FsManualScanTriggerUI,
  [FsScheduledScanTriggerUI.kind]: FsScheduledScanTriggerUI,
  [FsWatchTriggerUI.kind]: FsWatchTriggerUI,
};

export function getWorkflowTriggerUI(kind: string): WorkflowTriggerUI<any> | undefined {
  return workflowTriggerRegistry[kind];
}

export type { WorkflowTriggerUI } from "./types";
