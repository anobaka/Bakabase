import { AcquisitionPlanProcessUI, DeliveryReviewResumeForm } from "./AcquisitionPlanProcess";
import type { WorkflowActivityUI } from "./types";
import { useTranslation } from "react-i18next";

/** Shares the processing UI, but every answer is fenced to the reviewed immutable artifact. */
export const AssessDeliveryUI: WorkflowActivityUI<Record<string, unknown>> = {
  ...AcquisitionPlanProcessUI,
  kind: "fs.assessDelivery",
  displayNameKey: "workflow.activity.fsAssessDelivery.name",
  Summary: () => {
    const { t } = useTranslation();
    return <span className="text-xs text-default-500">{t("workflow.activity.fsAssessDelivery.name")}</span>;
  },
  ResumeForm: DeliveryReviewResumeForm,
};
