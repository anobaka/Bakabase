import type { WorkflowActivityUI } from "../types";
import { downloadWorksetExecutionLabels } from "../executionLabels";
import { useEffect, useState } from "react";
import { ResumeGuidance } from "../../ResumePresentation";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Select, Switch } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";
import { fileCleanupCoverageIncomplete } from "../../waitReasonLabel";

export interface CleanupConfig {
  policy: "keep" | "quarantine" | "delete";
  useTargetStoragePolicy?: boolean;
}

export const CleanupArtifactsUI: WorkflowActivityUI<CleanupConfig> = {
  kind: "download.cleanupArtifacts",
  executionLabels: downloadWorksetExecutionLabels,
  displayNameKey: "cloudDownload.activity.download.cleanupArtifacts.name",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ policy: "keep" }),
  parseConfig: (json) => {
    try {
      const value = JSON.parse(json || "{}");
      return {
        policy: value?.policy ?? "keep",
        ...(value?.useTargetStoragePolicy != null
          ? { useTargetStoragePolicy: value.useTargetStoragePolicy }
          : {}),
      };
    } catch {
      return { policy: "keep" };
    }
  },
  serializeConfig: JSON.stringify,
  isValid: ({ policy, useTargetStoragePolicy }) =>
    ["keep", "quarantine", "delete"].includes(policy) &&
    (useTargetStoragePolicy == null || typeof useTargetStoragePolicy === "boolean"),
  ConfigForm: ({ value, onChange }) => {
    const { t } = useTranslation();
    return (
      <div className="space-y-3">
        <p className="text-sm text-default-500">
          {t("cloudDownload.activity.download.cleanupArtifacts.description")}
        </p>
        <Switch
          isSelected={value.useTargetStoragePolicy === true}
          onValueChange={(useTargetStoragePolicy) => onChange({ ...value, useTargetStoragePolicy })}
        >
          {t("workflow.parameters.cleanupFromSource")}
        </Switch>
        <Select
          size="sm"
          label={t(
            value.useTargetStoragePolicy
              ? "workflow.parameters.cleanupFallback"
              : "acquisition.storage.sourceCleanup",
          )}
          selectedKeys={[value.policy]}
          disallowEmptySelection
          dataSource={["keep", "quarantine", "delete"].map((policy) => ({
            value: policy,
            label: t(`acquisition.storage.sourceCleanup.${policy}`),
            description: t(`acquisition.storage.sourceCleanup.${policy}Hint`),
          }))}
          onSelectionChange={(keys) => {
            const policy = String(Array.from(keys)[0]);
            if (["keep", "quarantine", "delete"].includes(policy))
              onChange({ ...value, policy: policy as CleanupConfig["policy"] });
          }}
        />
        <p className="text-xs text-default-500">{t("cloudDownload.cleanup.safetyHint")}</p>
      </div>
    );
  },
  Summary: ({ config }) => {
    const { t } = useTranslation();
    return (
      <span className="text-xs text-default-500">
        {t(
          config.useTargetStoragePolicy
            ? "workflow.parameters.cleanupFromSource"
            : `acquisition.storage.sourceCleanup.${config.policy}`,
        )}
      </span>
    );
  },
  ResumeForm: ({ promptJson, submitting, onSubmit }) => {
    const { t } = useTranslation();
    const [approved, setApproved] = useState(false);
    const incompleteCoverage = fileCleanupCoverageIncomplete(promptJson);
    const guidance = incompleteCoverage ? "cleanupCoverage" : "cleanup";
    useEffect(() => setApproved(false), [promptJson]);
    return (
      <div className="min-w-0 space-y-5">
        <ResumeGuidance
          why={t(`cloudDownload.resumeGuide.${guidance}.why`)}
          action={t(`cloudDownload.resumeGuide.${guidance}.action`)}
          next={t(`cloudDownload.resumeGuide.${guidance}.next`)}
        />
        {!incompleteCoverage && (
          <p className="text-sm text-default-500">{t("cloudDownload.cleanup.waitingHint")}</p>
        )}
        <div className="space-y-3 border-t border-default-200 pt-5">
          <Checkbox isSelected={approved} onValueChange={setApproved} isDisabled={submitting}>
            {t("cloudDownload.resumeGuide.confirmCleanup")}
          </Checkbox>
          <Button
            className="h-auto whitespace-normal px-4 py-2.5"
            color="primary"
            isLoading={submitting}
            isDisabled={submitting || !approved}
            onPress={() => onSubmit("{}")}
          >
            {t("cloudDownload.resumeGuide.retryCleanup")}
          </Button>
        </div>
      </div>
    );
  },
};
