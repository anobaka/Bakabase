import type { WorkflowActivityUI } from "../types";

import { useTranslation } from "react-i18next";

import { Switch } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

type ReadConfig = { useConfiguredSoulPlusPurchaseLimit: boolean };

export const PostParserReadContentUI: WorkflowActivityUI<ReadConfig> = {
  kind: "postParser.readContent",
  displayNameKey: "workflow.activity.postParserReadContent.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ useConfiguredSoulPlusPurchaseLimit: false }),
  parseConfig: (json) => {
    try {
      return {
        useConfiguredSoulPlusPurchaseLimit:
          JSON.parse(json || "{}").useConfiguredSoulPlusPurchaseLimit === true,
      };
    } catch {
      return { useConfiguredSoulPlusPurchaseLimit: false };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm: ({ value, onChange }) => {
    const { t } = useTranslation();

    return (
      <div className="space-y-3">
        <p className="text-sm leading-relaxed text-default-500">
          {t("workflow.activity.postParserReadContent.description")}
        </p>
        <Switch
          isSelected={value.useConfiguredSoulPlusPurchaseLimit}
          size="sm"
          onValueChange={(checked) => onChange({ useConfiguredSoulPlusPurchaseLimit: checked })}
        >
          {t("workflow.postParser.purchaseLimit")}
        </Switch>
      </div>
    );
  },
  Summary: ({ config }) => {
    const { t } = useTranslation();

    return (
      <span className="text-xs text-default-500">
        {t(
          config.useConfiguredSoulPlusPurchaseLimit
            ? "workflow.postParser.purchaseLimit"
            : "workflow.postParser.noPurchase",
        )}
      </span>
    );
  },
};

const ExtractExplanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-sm leading-relaxed text-default-500">
      {t("workflow.activity.postParserExtractDownloadInfo.description")}
    </p>
  );
};

export const PostParserExtractDownloadInfoUI: WorkflowActivityUI<Record<string, never>> = {
  kind: "postParser.extractDownloadInfo",
  displayNameKey: "workflow.activity.postParserExtractDownloadInfo.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({}),
  parseConfig: () => ({}),
  serializeConfig: () => "{}",
  isValid: () => true,
  ConfigForm: ExtractExplanation,
  Summary: ExtractExplanation,
};
