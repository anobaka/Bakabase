import type { WorkflowActivityUI } from "./types";

import { ExHentaiEnqueueDownloadUI } from "./ExHentaiEnqueueDownload";
import { SubscriptionItemTitleContainsUI } from "./SubscriptionItemTitleContains";
import { AiTransformUI } from "./AiTransform";
import { ExHentaiQueryToGalleryUI } from "./ExHentaiQueryToGallery";
import { CreateNotificationUI } from "./CreateNotification";
import { FsFileNameOpUI } from "./FsFileNameOp";
import { FsSaveNameUI } from "./FsSaveName";
import { TextRemoveWrappedUI } from "./TextOps/RemoveWrapped";
import { TextRemoveTextsUI } from "./TextOps/RemoveTexts";
import { TextTrimUI } from "./TextOps/Trim";

export const workflowActivityRegistry: Record<string, WorkflowActivityUI<any>> = {
  [SubscriptionItemTitleContainsUI.kind]: SubscriptionItemTitleContainsUI,
  [AiTransformUI.kind]: AiTransformUI,
  [ExHentaiQueryToGalleryUI.kind]: ExHentaiQueryToGalleryUI,
  [ExHentaiEnqueueDownloadUI.kind]: ExHentaiEnqueueDownloadUI,
  [CreateNotificationUI.kind]: CreateNotificationUI,
  [FsFileNameOpUI.kind]: FsFileNameOpUI,
  [FsSaveNameUI.kind]: FsSaveNameUI,
  [TextRemoveWrappedUI.kind]: TextRemoveWrappedUI,
  [TextRemoveTextsUI.kind]: TextRemoveTextsUI,
  [TextTrimUI.kind]: TextTrimUI,
};

export function getWorkflowActivityUI(kind: string): WorkflowActivityUI<any> | undefined {
  return workflowActivityRegistry[kind];
}

export type { WorkflowActivityUI } from "./types";
