import type { WorkflowTriggerUI } from "../types";
import { useTranslation } from "react-i18next";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";
const Explanation = () => {
  const { t } = useTranslation();
  return <p className="text-sm text-default-500">{t("cloudDownload.trigger.description")}</p>;
};
export const DownloadSourcesTriggerUI: WorkflowTriggerUI<Record<string, never>> = {
  kind: "download.sources",
  displayNameKey: "cloudDownload.trigger.name",
  defaultFilter: () => ({}),
  parseFilter: () => ({}),
  serializeFilter: () => null,
  isValid: () => true,
  resolveOutputItemType: () => WorkflowItemTypes.DownloadWorkset,
  FilterForm: Explanation,
  FilterSummary: Explanation,
  runEntry: {
    path: "/downloader",
    labelKey: "cloudDownload.title",
    descriptionKey: "cloudDownload.trigger.description",
  },
};
