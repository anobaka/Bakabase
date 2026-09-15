import type { WorkflowTriggerUI } from "./types";

import { SubscriptionUpdatedTriggerUI } from "./SubscriptionUpdated";
import { DownloaderCompletedTriggerUI } from "./DownloaderCompleted";
import { DownloaderResultReadyTriggerUI } from "./DownloaderResultReady";
import { FsManualScanTriggerUI } from "./FsManualScan";
import { FsScheduledScanTriggerUI } from "./FsScheduledScan";
import { FsWatchTriggerUI } from "./FsWatch";
import { ResourceMaterializedTriggerUI } from "./ResourceMaterialized";
import { AcquisitionRequestedTriggerUI } from "./AcquisitionRequested";
import { AcquisitionStatusChangedTriggerUI } from "./AcquisitionStatusChanged";
import { CollectionMembersAddedTriggerUI } from "./CollectionMembersAdded";
import { PostParserManualTriggerUI } from "./PostParserManual";

export const workflowTriggerSources = {
  acquisition: { path: "/acquisitions", labelKey: "workflowTriggers.source.acquisition" },
  downloader: { path: "/downloader", labelKey: "workflowTriggers.source.downloader" },
  postParser: { path: "/post-parser", labelKey: "workflowTriggers.source.postParser" },
  subscription: { path: "/subscriptions", labelKey: "workflowTriggers.source.subscription" },
  collection: { path: "/collections", labelKey: "workflowTriggers.source.collection" },
  resource: { path: "/resource", labelKey: "workflowTriggers.source.resource" },
  fs: { path: "/workflows", labelKey: "workflowTriggers.source.fs" },
} as const;

const withGuide = <T>(
  ui: WorkflowTriggerUI<T>,
  source: keyof typeof workflowTriggerSources,
): WorkflowTriggerUI<T> => ({
  ...ui,
  guide: {
    sourceEntry: workflowTriggerSources[source],
    inputKey: `workflowTriggers.guide.${ui.kind}.input`,
    configureKey: `workflowTriggers.guide.${ui.kind}.configure`,
    helpTarget: { topic: "workflow", section: "triggers" },
  },
});

/**
 * Registry of trigger UIs keyed by their backend `kind`.
 * Triggers that the server reports but the frontend hasn't shipped a UI for
 * fall back to a read-only "raw JSON" display in the editor.
 */
export const workflowTriggerRegistry: Record<string, WorkflowTriggerUI<any>> = {
  [PostParserManualTriggerUI.kind]: withGuide(PostParserManualTriggerUI, "postParser"),
  [SubscriptionUpdatedTriggerUI.kind]: withGuide(SubscriptionUpdatedTriggerUI, "subscription"),
  [DownloaderCompletedTriggerUI.kind]: withGuide(DownloaderCompletedTriggerUI, "downloader"),
  [DownloaderResultReadyTriggerUI.kind]: withGuide(DownloaderResultReadyTriggerUI, "downloader"),
  [FsManualScanTriggerUI.kind]: withGuide(FsManualScanTriggerUI, "fs"),
  [FsScheduledScanTriggerUI.kind]: withGuide(FsScheduledScanTriggerUI, "fs"),
  [FsWatchTriggerUI.kind]: withGuide(FsWatchTriggerUI, "fs"),
  [ResourceMaterializedTriggerUI.kind]: withGuide(ResourceMaterializedTriggerUI, "resource"),
  [AcquisitionRequestedTriggerUI.kind]: withGuide(AcquisitionRequestedTriggerUI, "acquisition"),
  [AcquisitionStatusChangedTriggerUI.kind]: withGuide(
    AcquisitionStatusChangedTriggerUI,
    "acquisition",
  ),
  [CollectionMembersAddedTriggerUI.kind]: withGuide(CollectionMembersAddedTriggerUI, "collection"),
};

export function getWorkflowTriggerUI(kind: string): WorkflowTriggerUI<any> | undefined {
  return workflowTriggerRegistry[kind];
}

export type { WorkflowTriggerUI } from "./types";
