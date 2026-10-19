import type { WorkflowActivityUI } from "../types";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Input, Switch } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";
import AccountSelect from "@/components/CloudDownload/AccountSelect";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import BaiduNetdiskConfig from "@/components/ThirdPartyConfig/platforms/BaiduNetdiskConfig";
import { Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DependencyComponent from "@/pages/configuration/components/Dependency/components/Component";

export interface CloudConfig {
  accountId?: string;
  remoteDirectory?: string;
  outputDirectory?: string;
  sourceIds?: string[];
  preferProviders?: string[];
  allowUngrouped?: boolean;
}
export interface CloudTarget {
  id: string;
  title?: string;
  selectedSourceId?: string;
  problem?: string;
  downloadTaskId?: number;
  files?: string[];
  sources: {
    id: string;
    url: string;
    providerId?: string;
    accessCode?: string;
    groupId?: string;
  }[];
}
export interface CloudPrompt {
  reason?: string;
  targets?: CloudTarget[];
  providers?: { providerId: string; available: boolean; problem?: string }[];
}

export function parseCloudPrompt(json: string | null): CloudPrompt {
  try {
    const value: unknown = JSON.parse(json ?? "{}");
    return value && typeof value === "object" && !Array.isArray(value)
      ? (value as CloudPrompt)
      : {};
  } catch {
    return {};
  }
}

/** Every selected target has one explicit mirror. Choosing another mirror replaces its selection. */
export function toggleCloudSource(
  previous: string[],
  targets: CloudTarget[],
  targetId: string,
  sourceId: string,
  checked: boolean,
) {
  const siblings = new Set(
    targets.find((target) => target.id === targetId)?.sources.map((source) => source.id) ?? [],
  );
  return checked
    ? [...previous.filter((id) => !siblings.has(id)), sourceId]
    : previous.filter((id) => id !== sourceId);
}

const CloudResumeForm: NonNullable<WorkflowActivityUI<CloudConfig>["ResumeForm"]> = ({
  promptJson,
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const prompt = parseCloudPrompt(promptJson);
  const targets = Array.isArray(prompt.targets) ? prompt.targets : [];
  const canChooseSources = prompt.reason === "chooseSources";
  const [selected, setSelected] = useState<string[]>(
    targets.flatMap((target) => (target.selectedSourceId ? [target.selectedSourceId] : [])),
  );
  const [accountId, setAccountId] = useState<string>();
  const [outputDirectory, setOutputDirectory] = useState("");
  return (
    <div className="space-y-3">
      <p className="text-sm text-default-500">{t("cloudDownload.resume.description")}</p>
      {!!prompt.providers?.length && (
        <div className="space-y-1 text-xs">
          {prompt.providers.map((provider) => (
            <p
              key={provider.providerId}
              className={provider.available ? "text-success" : "text-warning-600"}
            >
              {provider.providerId}:{" "}
              {provider.available
                ? t("cloudDownload.available")
                : provider.problem
                  ? t(`cloudDownload.problem.${provider.problem}`, {
                      defaultValue: provider.problem,
                    })
                  : t("cloudDownload.unavailable")}
            </p>
          ))}
        </div>
      )}
      <div className="max-h-64 space-y-3 overflow-auto">
        {targets.map((target) => (
          <div key={target.id} className="space-y-2 rounded-lg bg-default-50 p-3">
            <p className="text-sm font-medium">{target.title || target.id}</p>
            {target.problem && (
              <p className="text-xs text-warning-600">
                {t(`cloudDownload.problem.${target.problem}`, { defaultValue: target.problem })}
              </p>
            )}
            {target.sources.map((source) => (
              <Checkbox
                key={source.id}
                isDisabled={submitting || !canChooseSources}
                isSelected={selected.includes(source.id)}
                onValueChange={(checked) =>
                  setSelected((previous) =>
                    toggleCloudSource(previous, targets, target.id, source.id, checked),
                  )
                }
              >
                <span className="break-all text-xs">
                  {source.providerId && `${source.providerId} · `}
                  {source.url}
                  {source.accessCode && ` · ${source.accessCode}`}
                </span>
              </Checkbox>
            ))}
            {target.downloadTaskId != null && (
              <a
                className="text-xs text-primary"
                href={`#/downloader?id=site:${target.downloadTaskId}`}
              >
                {t("cloudDownload.task", { id: target.downloadTaskId })}
              </a>
            )}
          </div>
        ))}
      </div>
      <AccountSelect value={accountId} onChange={setAccountId} />
      <Button
        size="sm"
        variant="light"
        onPress={() =>
          createPortal(Modal, {
            defaultVisible: true,
            size: "3xl",
            title: t("cloudDownload.provider.baidu"),
            footer: false,
            children: <BaiduNetdiskConfig />,
          })
        }
      >
        {t("cloudDownload.account.manage")}
      </Button>
      {Array.from(
        new Set(targets.flatMap((target) => target.sources.map((source) => source.providerId))),
      ).map((provider) => {
        const id =
          provider === "baidu"
            ? "baidu-pcs-go-component-service"
            : provider === "googleDrive"
              ? "gdown-component-service"
              : provider === "gofile"
                ? "gallery-dl-component-service"
                : undefined;
        return id ? (
          <details key={id} className="rounded-lg bg-default-50 p-3">
            <summary className="cursor-pointer text-sm">
              {t("cloudDownload.dependency", { provider })}
            </summary>
            <DependencyComponent id={id} />
          </details>
        ) : null;
      })}
      <DownloadDirectoryField
        value={outputDirectory}
        onChange={setOutputDirectory}
        label={t("bt.outputDirectory")}
        description={t("cloudDownload.resume.keepDirectory")}
      />
      <a href="#/configuration?tab=dependency" className="inline-block text-sm text-primary">
        {t("cloudDownload.dependencies")}
      </a>
      <Button
        color="primary"
        isLoading={submitting}
        isDisabled={submitting || (canChooseSources && selected.length === 0)}
        onPress={() =>
          onSubmit(
            JSON.stringify({
              sourceIds: canChooseSources ? selected : undefined,
              accountId,
              outputDirectory: outputDirectory.trim() || undefined,
            }),
          )
        }
      >
        {t("cloudDownload.resume.continue")}
      </Button>
    </div>
  );
};

function activity(kind: string, defaults: CloudConfig = {}): WorkflowActivityUI<CloudConfig> {
  const isSelection = kind === "download.selectSources";
  const isTransfer = kind === "baidu.transfer";
  const isDownload = kind === "baidu.download" || kind === "download.enqueueSupported";
  const ConfigForm: WorkflowActivityUI<CloudConfig>["ConfigForm"] = ({ value, onChange }) => {
    const { t } = useTranslation();
    return (
      <div className="space-y-3">
        <p className="text-xs text-default-500">
          {t(`cloudDownload.activity.${kind}.description`)}
        </p>
        {(isTransfer || isDownload) && (
          <AccountSelect
            value={value.accountId}
            onChange={(accountId) => onChange({ ...value, accountId })}
          />
        )}
        {isTransfer && (
          <Input
            label={t("cloudDownload.remoteDirectory")}
            value={value.remoteDirectory ?? "/Bakabase"}
            onValueChange={(remoteDirectory) => onChange({ ...value, remoteDirectory })}
          />
        )}
        {isDownload && (
          <DownloadDirectoryField
            label={t("bt.outputDirectory")}
            value={value.outputDirectory ?? ""}
            onChange={(outputDirectory) => onChange({ ...value, outputDirectory })}
            description={t("cloudDownload.resume.keepDirectory")}
          />
        )}
        {isSelection && (
          <>
            <Input
              label={t("cloudDownload.preferProviders")}
              placeholder="baidu,googleDrive,gofile"
              value={(value.preferProviders ?? []).join(",")}
              onValueChange={(raw) =>
                onChange({
                  ...value,
                  preferProviders: raw
                    .split(",")
                    .map((provider) => provider.trim())
                    .filter(Boolean),
                })
              }
            />
            <Switch
              isSelected={!!value.allowUngrouped}
              onValueChange={(allowUngrouped) => onChange({ ...value, allowUngrouped })}
            >
              {t("cloudDownload.allowUngrouped")}
            </Switch>
          </>
        )}
      </div>
    );
  };
  return {
    kind,
    displayNameKey: `cloudDownload.activity.${kind}.name`,
    category:
      kind === "download.prepareSources"
        ? WorkflowActivityCategory.Transform
        : WorkflowActivityCategory.Action,
    defaultConfig: () => ({ ...defaults }),
    parseConfig: (json) => {
      try {
        return { ...defaults, ...JSON.parse(json || "{}") };
      } catch {
        return { ...defaults };
      }
    },
    serializeConfig: JSON.stringify,
    isValid: (config) => !isTransfer || !!config.remoteDirectory?.startsWith("/"),
    ConfigForm,
    Summary: ({ config }) => (
      <span className="truncate text-xs text-default-500">
        {isTransfer ? config.remoteDirectory : config.outputDirectory}
      </span>
    ),
    ResumeForm: CloudResumeForm,
  };
}

export const cloudDownloadActivities = [
  activity("download.prepareSources"),
  activity("download.selectSources", {
    preferProviders: ["baidu", "googleDrive", "gofile"],
    allowUngrouped: false,
  }),
  activity("baidu.transfer", { remoteDirectory: "/Bakabase" }),
  activity("baidu.download"),
  activity("download.enqueueSupported"),
  activity("download.awaitCompletion"),
];
