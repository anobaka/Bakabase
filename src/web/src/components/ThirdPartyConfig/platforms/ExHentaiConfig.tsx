"use client";

import { useEffect, useMemo, useState, type FC } from "react";
import { useTranslation } from "react-i18next";
import { CheckboxGroup, Textarea } from "@heroui/react";

import AccountsPanel, { type AccountField } from "../base/AccountsPanel";
import ConfigurableThirdPartyPanel, {
  type ConfigFieldTab,
} from "../base/ConfigurableThirdPartyPanel";
import MetadataMappingPanel from "../base/MetadataMappingPanel";
import TampermonkeyInstallButton from "../base/TampermonkeyInstallButton";
import AutoSyncPanel from "../base/AutoSyncPanel";
import ThirdPartyConfigModal from "../base/ThirdPartyConfigModal";
import ProxyField from "../base/ProxyField";
import DownloadResultWorkflowField from "../base/DownloadResultWorkflowField";
import useAutoSaveToast from "../base/useAutoSaveToast";

import { Checkbox, Chip, NumberInput, toast } from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import BApi from "@/sdk/BApi";
import { useExHentaiOptionsStore } from "@/stores/options";
import { CookieValidatorTarget, ResourceSource, ThirdPartyId } from "@/sdk/constants";
import PreferTorrentField from "@/pages/downloader/components/TaskDetailModal/components/PreferTorrentField";

export enum ExHentaiConfigField {
  Accounts = "accounts",
  DataFetch = "dataFetch",
  Download = "download",
  MetadataSync = "metadataSync",
  AutoSync = "autoSync",
  Integration = "integration",
}

export interface ExHentaiConfigPanelProps {
  fields?: ExHentaiConfigField[] | "all";
}

export const ExHentaiConfigPanel: FC<ExHentaiConfigPanelProps> = ({ fields = "all" }) => {
  const { t } = useTranslation();
  const options = useExHentaiOptionsStore((s) => s.data);
  const patch = useExHentaiOptionsStore((s) => s.patch);
  const patchWithToast = useAutoSaveToast(BApi.options.patchExHentaiOptions);
  const [namingDefinition, setNamingDefinition] = useState<any>();

  const showDownload =
    fields === "all" || (Array.isArray(fields) && fields.includes(ExHentaiConfigField.Download));

  useEffect(() => {
    if (!showDownload) return;
    BApi.downloadTask.getAllDownloaderDefinitions().then((res) => {
      const def = (res.data || []).find((d) => d.thirdPartyId === 2); // ExHentai = 2

      if (def) setNamingDefinition(def);
    });
  }, [showDownload]);

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
        cookieValidatorTarget: CookieValidatorTarget.ExHentai,
        cookieCaptureTarget: CookieValidatorTarget.ExHentai,
      },
    ],
    [t],
  );

  const handleAccountsSave = async (accounts: any[]) => {
    await patch({ accounts });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const tabs: ConfigFieldTab<ExHentaiConfigField>[] = useMemo(
    () => [
      {
        field: ExHentaiConfigField.Accounts,
        key: "accounts",
        title: t("resourceSource.config.tab.accounts"),
        content: (
          <AccountsPanel
            accounts={options?.accounts || []}
            fields={accountFields}
            onSave={handleAccountsSave}
          />
        ),
      },
      {
        field: ExHentaiConfigField.MetadataSync,
        key: "metadata",
        title: t("resourceSource.config.tab.metadataSync"),
        content: <MetadataMappingPanel source={ResourceSource.ExHentai} />,
      },
      {
        field: ExHentaiConfigField.AutoSync,
        key: "autoSync",
        title: t("thirdPartyConfig.autoSync.tabTitle"),
        content: (
          <AutoSyncPanel
            autoSyncIntervalMinutes={options?.autoSyncIntervalMinutes}
            // The patch applies only values that are present, so clearing has to be sent as 0
            // rather than null — which the backend already treats as disabled.
            onSave={(v) => patch({ autoSyncIntervalMinutes: v ?? 0 })}
          />
        ),
      },
      {
        field: ExHentaiConfigField.DataFetch,
        key: "dataFetch",
        title: t("thirdPartyConfig.group.dataFetch"),
        content: (
          <div className="space-y-4">
            <NumberInput
              description={t<string>("thirdPartyConfig.field.maxConcurrency.description")}
              label={t<string>("thirdPartyConfig.label.maxConcurrency")}
              max={100}
              min={1}
              value={options?.maxConcurrency || 1}
              onValueChange={(v) => patchWithToast({ maxConcurrency: v })}
            />
            <NumberInput
              description={t<string>("thirdPartyConfig.field.requestInterval.description")}
              label={t<string>("thirdPartyConfig.label.requestInterval")}
              min={0}
              value={options?.requestInterval ?? 1000}
              onValueChange={(v) => patchWithToast({ requestInterval: v })}
            />
            <NumberInput
              description={t<string>("thirdPartyConfig.field.maxRetries.description")}
              label={t<string>("thirdPartyConfig.label.maxRetries")}
              min={0}
              value={options?.maxRetries || 0}
              onValueChange={(v) => patchWithToast({ maxRetries: v })}
            />
            <NumberInput
              description={t<string>("thirdPartyConfig.field.requestTimeout.description")}
              label={t<string>("thirdPartyConfig.label.requestTimeout")}
              min={0}
              value={options?.requestTimeout || 0}
              onValueChange={(v) => patchWithToast({ requestTimeout: v })}
            />
          </div>
        ),
      },
      {
        field: ExHentaiConfigField.Download,
        key: "download",
        title: t("thirdPartyConfig.group.download"),
        content: (
          <div className="space-y-4">
            <DownloadResultWorkflowField
              value={options?.downloadResultWorkflowId}
              onChange={(workflowId) => patch({ downloadResultWorkflowId: workflowId ?? 0 })}
            />
            <div>
              <span className="text-sm font-medium">
                {t<string>("thirdPartyConfig.field.defaultPath.label")}
              </span>
              <div className="mt-1">
                <FileSystemSelectorButton
                  fileSystemSelectorProps={{
                    targetType: "folder",
                    onSelected: (e) => patchWithToast({ defaultPath: e.path }),
                    defaultSelectedPath: options?.defaultPath,
                  }}
                />
              </div>
              <span className="text-xs text-default-400 mt-1 block">
                {t<string>("thirdPartyConfig.field.defaultPath.description")}
              </span>
            </div>
            <Textarea
              description={
                namingDefinition?.namingFields?.length ? (
                  <div>
                    <div>{t<string>("thirdPartyConfig.field.namingConvention.description")}</div>
                    <div className="flex flex-wrap gap-1 mt-2">
                      {namingDefinition.namingFields.map((x, i) => (
                        <Chip
                          key={i}
                          color="secondary"
                          size="sm"
                          variant="flat"
                          onClick={() =>
                            patchWithToast({
                              namingConvention:
                                (options?.namingConvention || "") + `{${x.name || x.key}}`,
                            })
                          }
                        >
                          {x.name || x.key}
                        </Chip>
                      ))}
                    </div>
                  </div>
                ) : (
                  t<string>("thirdPartyConfig.field.namingConvention.description")
                )
              }
              label={t<string>("thirdPartyConfig.field.namingConvention.label")}
              placeholder={namingDefinition?.defaultConvention}
              size="sm"
              value={options?.namingConvention || ""}
              onValueChange={(v) => patchWithToast({ namingConvention: v })}
            />
            <ProxyField thirdPartyId={ThirdPartyId.ExHentai} />
            <PreferTorrentField
              preferTorrent={options?.preferTorrent ?? true}
              onChange={(v) => patchWithToast({ preferTorrent: v })}
            />
            <CheckboxGroup
              description={t<string>("downloader.tip.prioritizeTasksWithTorrentDesc")}
              isDisabled={!(options?.preferTorrent ?? true)}
              label={t<string>("downloader.label.prioritizeTasksWithTorrent")}
              orientation="horizontal"
              size="sm"
              value={options?.prioritizeTasksWithTorrent ? ["yes"] : []}
              onValueChange={(v) =>
                patchWithToast({ prioritizeTasksWithTorrent: v.includes("yes") })
              }
            >
              <Checkbox value="yes">{t("common.label.yes")}</Checkbox>
            </CheckboxGroup>
            <NumberInput
              description={t<string>("downloader.tip.torrentCheckValidityHoursDesc")}
              isDisabled={!(options?.preferTorrent ?? true)}
              label={t<string>("downloader.label.torrentCheckValidityHours")}
              min={0}
              // 0 is the documented "always re-check" value, matching the backend, so an empty
              // box does not need to mean something different from what the user can type.
              value={options?.torrentCheckValidityHours ?? 0}
              onValueChange={(v) =>
                patchWithToast({ torrentCheckValidityHours: Number.isNaN(v) ? 0 : v })
              }
            />
            <div className="flex flex-col gap-4 rounded-lg border border-default-200 p-3">
              <div className="flex flex-col gap-3">
                <Checkbox
                  className="m-0"
                  isSelected={options?.preferOriginalImages ?? false}
                  onValueChange={(v) => patchWithToast({ preferOriginalImages: v })}
                >
                  {t("thirdPartyConfig.exHentai.originalImages.prefer")}
                </Checkbox>
                <div
                  className="rounded-lg border border-warning-300 bg-warning-50 p-3 text-sm text-warning-700"
                  role="note"
                >
                  {t("thirdPartyConfig.exHentai.originalImages.warning")}
                </div>
                <p className="text-xs text-default-500">
                  {t("thirdPartyConfig.exHentai.originalImages.scope")}
                </p>
              </div>
              {options?.preferOriginalImages && (
                <div className="flex flex-col gap-4">
                  <div className="flex flex-col gap-2">
                    <p className="text-sm text-default-500">
                      {t("thirdPartyConfig.exHentai.originalImages.freeOnly")}
                    </p>
                    <p className="text-xs leading-relaxed text-default-500">
                      {t("thirdPartyConfig.exHentai.originalImages.freeConfirmation")}
                    </p>
                    <p className="text-sm text-default-500">
                      {t("thirdPartyConfig.exHentai.originalImages.unavailable")}
                    </p>
                  </div>
                  <Checkbox
                    className="m-0"
                    isSelected={options?.allowOriginalImageGpSpending ?? false}
                    onValueChange={(v) => patchWithToast({ allowOriginalImageGpSpending: v })}
                  >
                    {t("thirdPartyConfig.exHentai.originalImages.allowGp")}
                  </Checkbox>
                  {options?.allowOriginalImageGpSpending && (
                    <div className="flex flex-col gap-3">
                      <NumberInput
                        description={t<string>(
                          "thirdPartyConfig.exHentai.originalImages.minimumGpDescription",
                        )}
                        formatOptions={{ maximumFractionDigits: 0 }}
                        label={t<string>("thirdPartyConfig.exHentai.originalImages.minimumGp")}
                        maxValue={Number.MAX_SAFE_INTEGER}
                        minValue={0}
                        step={1}
                        value={options?.originalImageMinimumGpBalance ?? 10000}
                        onValueChange={(v) => {
                          const value = Number.isNaN(v) ? 10000 : v;

                          if (Number.isSafeInteger(value) && value >= 0) {
                            patchWithToast({ originalImageMinimumGpBalance: value });
                          }
                        }}
                      />
                      <NumberInput
                        description={t<string>(
                          "thirdPartyConfig.exHentai.originalImages.maximumGpDescription",
                        )}
                        formatOptions={{ maximumFractionDigits: 0 }}
                        label={t<string>("thirdPartyConfig.exHentai.originalImages.maximumGp")}
                        maxValue={Number.MAX_SAFE_INTEGER}
                        minValue={0}
                        step={1}
                        value={options?.originalImageMaximumGpCostPerTask ?? 100000}
                        onValueChange={(v) => {
                          const value = Number.isNaN(v) ? 100000 : v;

                          if (Number.isSafeInteger(value) && value >= 0) {
                            patchWithToast({ originalImageMaximumGpCostPerTask: value });
                          }
                        }}
                      />
                      <p className="text-xs text-default-500">
                        {t("thirdPartyConfig.exHentai.originalImages.gpPolicy")}
                      </p>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        ),
      },
      {
        field: ExHentaiConfigField.Integration,
        key: "integration",
        title: t("thirdPartyIntegration.label.tampermonkeyScript"),
        content: (
          <TampermonkeyInstallButton
            descriptions={[t("thirdPartyIntegration.tip.exHentaiClick")]}
          />
        ),
      },
    ],
    [options, accountFields, t, patch, patchWithToast, namingDefinition],
  );

  return <ConfigurableThirdPartyPanel fields={fields} tabs={tabs} />;
};

export interface ExHentaiConfigModalProps {
  onDestroyed?: () => void;
  onClose?: () => void;
  isOpen?: boolean;
  fields?: ExHentaiConfigField[] | "all";
}

export const ExHentaiConfigModal: FC<ExHentaiConfigModalProps> = ({
  onDestroyed,
  onClose,
  isOpen,
  fields,
}) => {
  const { t } = useTranslation();
  const handleClose = onClose ?? onDestroyed;

  return (
    <ThirdPartyConfigModal
      isOpen={isOpen}
      title={t("resourceSource.exhentai.title")}
      onClose={handleClose}
    >
      <ExHentaiConfigPanel fields={fields} />
    </ThirdPartyConfigModal>
  );
};

const ExHentaiConfig = ExHentaiConfigModal;

export default ExHentaiConfig;
