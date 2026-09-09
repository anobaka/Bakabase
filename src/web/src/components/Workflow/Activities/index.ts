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
import { TextCaptureUI } from "./TextOps/Capture";
import { TextTemplateUI } from "./TextOps/Template";
import { FsExpandChildrenUI } from "./FsExpandChildren";
import { acquisitionStepUI, isAcquisitionStepKind } from "./AcquisitionStep";
import { AcquisitionResolveSharedContentUI } from "./AcquisitionResolveSharedContent";
import { AcquisitionSelectLinkUI } from "./AcquisitionSelectLink";
import { AcquisitionWaitForInboxUI } from "./AcquisitionWaitForInbox";

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
  [TextCaptureUI.kind]: TextCaptureUI,
  [TextTemplateUI.kind]: TextTemplateUI,
  [FsExpandChildrenUI.kind]: FsExpandChildrenUI,
  [AcquisitionResolveSharedContentUI.kind]: AcquisitionResolveSharedContentUI,
  [AcquisitionSelectLinkUI.kind]: AcquisitionSelectLinkUI,
  [AcquisitionWaitForInboxUI.kind]: AcquisitionWaitForInboxUI,
};

/** Built once per kind so the editor's forms keep their state across renders. */
const acquisitionStepCache: Record<string, WorkflowActivityUI<any>> = {};

export function getWorkflowActivityUI(kind: string): WorkflowActivityUI<any> | undefined {
  const registered = workflowActivityRegistry[kind];

  if (registered) return registered;

  // The acquisition steps share one generic package until a step earns a form of its own; an
  // explicit entry above always wins, so adding one is how a step graduates.
  if (isAcquisitionStepKind(kind)) {
    return (acquisitionStepCache[kind] ??= acquisitionStepUI(kind));
  }

  return undefined;
}

export type { WorkflowActivityUI } from "./types";
