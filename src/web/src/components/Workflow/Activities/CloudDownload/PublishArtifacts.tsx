import type { WorkflowActivityUI } from "../types";
import { fileProcessingExecutionLabels } from "../executionLabels";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input, Select } from "@/components/bakaui";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import { ResumeGuidance, ResumeProblem } from "../../ResumePresentation";
import ResumeActions from "../../ResumeActions";
import { WorkflowActivityCategory } from "@/sdk/constants";
import { localStorageProblemCode } from "@/components/CloudDownload/problemLabels";
import DeliveryContentLayoutFields, {
  defaultDeliveryContentLayout,
  deliveryContentLayoutIsValid,
  type DeliveryContentLayout,
} from "@/components/FileArtifacts/DeliveryContentLayoutFields";

interface Config {
  targetDirectory: string;
  source: "processed" | "download";
  reusePolicy: "reuseVerified" | "force";
  contentLayout?: DeliveryContentLayout | null;
}
const defaults = (): Config => ({
  targetDirectory: "",
  source: "processed",
  reusePolicy: "reuseVerified",
  contentLayout: defaultDeliveryContentLayout(),
});
export const PublishFileArtifactsUI: WorkflowActivityUI<Config> = {
  kind: "fs.publishArtifacts",
  executionLabels: fileProcessingExecutionLabels,
  displayNameKey: "workflow.activity.fsPublishArtifacts.name",
  category: WorkflowActivityCategory.Action,
  defaultConfig: defaults,
  parseConfig: (json) => {
    try {
      return { ...defaults(), contentLayout: undefined, ...JSON.parse(json || "{}") };
    } catch {
      return { ...defaults(), contentLayout: undefined };
    }
  },
  serializeConfig: JSON.stringify,
  isValid: (value) =>
    typeof value.targetDirectory === "string" &&
    ["processed", "download"].includes(value.source) &&
    ["reuseVerified", "force"].includes(value.reusePolicy) &&
    deliveryContentLayoutIsValid(value.contentLayout),
  ConfigForm: ({ value, onChange }) => {
    const { t } = useTranslation();
    return (
      <div className="space-y-3">
        <p className="text-sm text-default-500">
          {t("workflow.activity.fsPublishArtifacts.description")}
        </p>
        <Input
          label={t("cloudDownload.publication.directory")}
          value={value.targetDirectory}
          placeholder={t("cloudDownload.publication.defaultDirectory")}
          onValueChange={(targetDirectory) => onChange({ ...value, targetDirectory })}
        />
        <p className="text-xs text-default-500">{t("cloudDownload.publication.directoryHint")}</p>
        <Select
          label={t("cloudDownload.publication.source")}
          selectedKeys={[value.source]}
          disallowEmptySelection
          onSelectionChange={(keys) => {
            const source = Array.from(keys)[0];
            if (source === "processed" || source === "download") onChange({ ...value, source });
          }}
          dataSource={[
            { value: "processed", label: t("cloudDownload.publication.source.processed") },
            { value: "download", label: t("cloudDownload.publication.source.download") },
          ]}
        />
        <DeliveryContentLayoutFields
          value={value.contentLayout}
          onChange={(contentLayout) => onChange({ ...value, contentLayout })}
        />
        <p className="text-xs text-default-500">{t("artifact.deliveryLayout.workflowHint")}</p>
      </div>
    );
  },
  Summary: ({ config }) => {
    const { t } = useTranslation();
    return (
      <span className="text-xs text-default-500">
        {config.targetDirectory || t("cloudDownload.publication.defaultDirectory")}
      </span>
    );
  },
  ResumeForm: ({ promptJson, submitting, onSubmit, fileTask }) => {
    const { t } = useTranslation();
    let prompt: { targetId?: string; targetDirectory?: string; message?: string; reason?: string } =
      {};
    try {
      prompt = JSON.parse(promptJson || "{}");
    } catch {
      /* Wait for a valid server prompt. */
    }
    const [directory, setDirectory] = useState(prompt.targetDirectory || "");
    const storageProblem =
      localStorageProblemCode(prompt.reason) ?? localStorageProblemCode(prompt.message);
    const guide =
      storageProblem === "LOCAL_STORAGE_RESERVE_LOW"
        ? "storageReserve"
        : prompt.reason === "StorageUnavailable"
          ? "storage"
          : "publication";
    useEffect(() => {
      try {
        setDirectory(JSON.parse(promptJson || "{}").targetDirectory || "");
      } catch {
        setDirectory("");
      }
    }, [promptJson]);
    if (fileTask && prompt.reason === "StorageUnavailable" && prompt.targetDirectory) {
      return (
        <div className="min-w-0 space-y-3">
          <p className="text-sm text-default-600">
            {t(storageProblem === "LOCAL_STORAGE_RESERVE_LOW"
              ? "acquisition.continue.deliveryReserveHint"
              : "acquisition.continue.deliveryStorageHint")}
          </p>
          <dl className="text-sm">
            <dt className="text-default-500">{t("cloudDownload.publication.directory")}</dt>
            <dd className="select-text break-all">{prompt.targetDirectory}</dd>
          </dl>
          <ResumeActions busy={submitting}>
            <Button color="primary" isLoading={submitting} isDisabled={submitting || !prompt.targetId}
              onPress={() => onSubmit(JSON.stringify({ targetId: prompt.targetId }))}>
              {t("cloudDownload.publication.retry")}
            </Button>
          </ResumeActions>
        </div>
      );
    }
    return (
      <div className="min-w-0 space-y-5">
        {!fileTask && <ResumeGuidance
          why={t(`cloudDownload.resumeGuide.${guide}.why`)}
          action={t(`cloudDownload.resumeGuide.${guide}.action`)}
          next={t(`cloudDownload.resumeGuide.${guide}.next`)}
        />}
        {prompt.message && (
          <ResumeProblem
            problem={prompt.message}
            fallback={t("cloudDownload.publication.conflictHint")}
          />
        )}
        <DownloadDirectoryField
          label={t("cloudDownload.publication.directory")}
          value={directory}
          onChange={setDirectory}
          description={t("cloudDownload.resumeGuide.publication.directory")}
        />
        <div className="space-y-3 border-t border-default-200 pt-5">
          <p className="text-xs text-default-500">
            {t("cloudDownload.resumeGuide.publicationEffect")}
          </p>
          <ResumeActions busy={submitting}><Button
            className="h-auto whitespace-normal px-4 py-2.5"
            color="primary"
            isDisabled={submitting || !prompt.targetId || !directory.trim()}
            isLoading={submitting}
            onPress={() =>
              onSubmit(
                JSON.stringify({ targetId: prompt.targetId, targetDirectory: directory.trim() }),
              )
            }
          >
            {t("cloudDownload.publication.retry")}
          </Button></ResumeActions>
        </div>
      </div>
    );
  },
};
