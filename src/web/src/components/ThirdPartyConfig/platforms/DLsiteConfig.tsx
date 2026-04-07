"use client";

import { useEffect, useMemo, useRef, useState, type FC } from "react";
import { useTranslation } from "react-i18next";
import { Button, Chip as HeroChip, Divider, Switch, Textarea } from "@heroui/react";
import { AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";

import { Chip, NumberInput, toast, Modal as BakaModal } from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import BApi from "@/sdk/BApi";
import { useDLsiteOptionsStore } from "@/stores/options";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { CookieValidatorTarget, ResourceSource } from "@/sdk/constants";
import AccountsPanel, { type AccountField } from "../base/AccountsPanel";
import MetadataMappingPanel from "../base/MetadataMappingPanel";
import AutoSyncPanel from "../base/AutoSyncPanel";
import ConfigurableThirdPartyPanel, { type ConfigFieldTab } from "../base/ConfigurableThirdPartyPanel";
import ThirdPartyConfigModal from "../base/ThirdPartyConfigModal";
import { LeStatusIndicator } from "@/pages/dlsite-works/components/LeStatusIndicator";

export enum DLsiteConfigField {
  Accounts = "accounts",
  DataFetch = "dataFetch",
  Download = "download",
  LocalFiles = "localFiles",
  MetadataSync = "metadataSync",
  AutoSync = "autoSync",
  LocaleEmulator = "localeEmulator",
}

export interface DLsiteConfigPanelProps {
  fields?: DLsiteConfigField[] | "all";
  showFooter?: boolean;
  onCancel?: () => void;
}

export const DLsiteConfigPanel: FC<DLsiteConfigPanelProps> = ({
  fields = "all",
  showFooter = true,
  onCancel,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const options = useDLsiteOptionsStore((s) => s.data);
  const patch = useDLsiteOptionsStore((s) => s.patch);
  const [saving, setSaving] = useState(false);
  const [namingDefinition, setNamingDefinition] = useState<any>();

  const showDownload = fields === "all" || (Array.isArray(fields) && fields.includes(DLsiteConfigField.Download));
  useEffect(() => {
    if (!showDownload) return;
    BApi.downloadTask.getAllDownloaderDefinitions().then((res) => {
      const def = (res.data || []).find((d) => d.thirdPartyId === 6); // DLsite = 6
      if (def) setNamingDefinition(def);
    });
  }, [showDownload]);
  const pendingAccountsRef = useRef<any[] | null>(null);

  const downloadDir = options?.defaultPath;
  const scanFolders = options?.scanFolders || [];

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
        cookieValidatorTarget: CookieValidatorTarget.DLsite,
        cookieCaptureTarget: CookieValidatorTarget.DLsite,
      },
    ],
    [t],
  );

  const handleSave = async () => {
    setSaving(true);
    try {
      const updates: any = {};
      if (pendingAccountsRef.current !== null) {
        updates.accounts = pendingAccountsRef.current;
      }
      if (Object.keys(updates).length > 0) {
        await patch(updates);
      }
      toast.success(t("thirdPartyConfig.success.saved"));
      onCancel?.();
    } finally {
      setSaving(false);
    }
  };

  const recommendedSubdir = "DLsite";

  const getRecommendedPath = (selectedPath: string): string | null => {
    const normalized = selectedPath.replace(/[\\/]+$/, "");
    const lastSegment = normalized.split(/[\\/]/).pop();
    if (lastSegment === recommendedSubdir) return null;
    const sep = selectedPath.includes("\\") ? "\\" : "/";
    return `${normalized}${sep}${recommendedSubdir}`;
  };

  const handleSelectDownloadDir = () => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      defaultSelectedPath: downloadDir,
      startPath: downloadDir,
      onSelected: async (e: any) => {
        const selected = e.path as string;
        const recommended = getRecommendedPath(selected);

        if (!recommended) {
          await patch({ defaultPath: selected });
          return;
        }

        createPortal(BakaModal, {
          defaultVisible: true,
          size: "lg",
          title: t("thirdPartyConfig.recommendedDir.title"),
          children: (
            <div className="space-y-3">
              <p className="text-sm">{t("thirdPartyConfig.recommendedDir.description")}</p>
              <div className="space-y-2 text-sm">
                <div className="flex items-center gap-2 rounded-md bg-success-50 p-2">
                  <span className="shrink-0 text-default-500">{t("thirdPartyConfig.recommendedDir.recommended")}:</span>
                  <code className="break-all">{recommended}</code>
                </div>
                <div className="flex items-center gap-2 rounded-md bg-default-100 p-2">
                  <span className="shrink-0 text-default-500">{t("thirdPartyConfig.recommendedDir.selected")}:</span>
                  <code className="break-all">{selected}</code>
                </div>
              </div>
            </div>
          ),
          footer: {
            actions: ["ok", "cancel"],
            cancelProps: {
              children: t("thirdPartyConfig.recommendedDir.useSelected"),
              onPress: async () => {
                await patch({ defaultPath: selected });
              },
            },
          },
          okProps: {
            children: t("thirdPartyConfig.recommendedDir.useRecommended"),
          },
          onOk: async () => {
            await patch({ defaultPath: recommended });
          },
        });
      },
    });
  };

  const handleAddScanFolder = () => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      onSelected: async (e: any) => {
        const updated = [...scanFolders, e.path];
        await patch({ scanFolders: updated });
      },
    });
  };

  const handleRemoveScanFolder = async (index: number) => {
    const updated = scanFolders.filter((_: string, i: number) => i !== index);
    await patch({ scanFolders: updated });
  };

  const tabs: ConfigFieldTab<DLsiteConfigField>[] = useMemo(
    () => [
      {
        field: DLsiteConfigField.Accounts,
        key: "accounts",
        title: t("resourceSource.config.tab.accounts"),
        content: (
          <AccountsPanel
            accounts={options?.accounts || []}
            fields={accountFields}
            hideFooter
            onAccountsChange={(accs) => {
              pendingAccountsRef.current = accs;
            }}
            onSave={async () => {}}
          />
        ),
      },
      {
        field: DLsiteConfigField.DataFetch,
        key: "dataFetch",
        title: t("thirdPartyConfig.group.dataFetch"),
        content: (
          <div className="space-y-4">
            <NumberInput label={t<string>("thirdPartyConfig.label.maxConcurrency")} description={t<string>("thirdPartyConfig.field.maxConcurrency.description")} min={1} max={100} value={options?.maxConcurrency || 1} onValueChange={(v) => patch({ maxConcurrency: v })} />
            <NumberInput label={t<string>("thirdPartyConfig.label.requestInterval")} description={t<string>("thirdPartyConfig.field.requestInterval.description")} min={0} value={options?.requestInterval || 1000} onValueChange={(v) => patch({ requestInterval: v })} />
            <NumberInput label={t<string>("thirdPartyConfig.label.maxRetries")} description={t<string>("thirdPartyConfig.field.maxRetries.description")} min={0} value={options?.maxRetries || 0} onValueChange={(v) => patch({ maxRetries: v })} />
            <NumberInput label={t<string>("thirdPartyConfig.label.requestTimeout")} description={t<string>("thirdPartyConfig.field.requestTimeout.description")} min={0} value={options?.requestTimeout || 0} onValueChange={(v) => patch({ requestTimeout: v })} />
          </div>
        ),
      },
      {
        field: DLsiteConfigField.Download,
        key: "download",
        title: t("thirdPartyConfig.group.download"),
        content: (
          <div className="space-y-4">
            <div>
              <span className="text-sm font-medium">{t<string>("thirdPartyConfig.field.defaultPath.label")}</span>
              <div className="mt-1">
                <FileSystemSelectorButton fileSystemSelectorProps={{ targetType: "folder", onSelected: (e) => patch({ defaultPath: e.path }), defaultSelectedPath: options?.defaultPath }} />
              </div>
              <span className="text-xs text-default-400 mt-1 block">{t<string>("thirdPartyConfig.field.defaultPath.description")}</span>
            </div>
            <Textarea
              label={t<string>("thirdPartyConfig.field.namingConvention.label")}
              placeholder={namingDefinition?.defaultConvention}
              description={
                namingDefinition?.namingFields?.length ? (
                  <div>
                    <div>{t<string>("thirdPartyConfig.field.namingConvention.description")}</div>
                    <div className="flex flex-wrap gap-1 mt-2">
                      {namingDefinition.namingFields.map((x, i) => (
                        <Chip key={i} color="secondary" size="sm" variant="flat"
                          onClick={() => patch({ namingConvention: (options?.namingConvention || "") + `{${x.name || x.key}}` })}>
                          {x.name || x.key}
                        </Chip>
                      ))}
                    </div>
                  </div>
                ) : t<string>("thirdPartyConfig.field.namingConvention.description")
              }
              size="sm"
              value={options?.namingConvention || ""}
              onValueChange={(v) => patch({ namingConvention: v })}
            />
          </div>
        ),
      },
      {
        field: DLsiteConfigField.LocalFiles,
        key: "localFiles",
        title: t("resourceSource.config.tab.localFiles"),
        content: (
          <div className="space-y-4">
            <div>
              <div className="mb-2 flex items-center justify-between">
                <span className="text-sm font-medium">{t("resourceSource.dlsite.config.downloadDir")}</span>
              </div>
              <Button className="w-full justify-start" size="sm" variant="flat" onPress={handleSelectDownloadDir}>
                {downloadDir || t("resourceSource.dlsite.config.downloadDirPlaceholder")}
              </Button>
            </div>

            <Divider />

            <Switch
              isSelected={options?.deleteArchiveAfterExtraction ?? false}
              size="sm"
              onValueChange={(v) => patch({ deleteArchiveAfterExtraction: v })}
            >
              <span className="text-sm font-medium">{t("resourceSource.dlsite.config.deleteArchiveAfterExtraction")}</span>
            </Switch>

            <Divider />

            <div>
              <div className="mb-2 flex items-center justify-between">
                <span className="text-sm font-medium">{t("resourceSource.dlsite.config.scanFolders")}</span>
                <Button size="sm" startContent={<AiOutlinePlus />} variant="flat" onPress={handleAddScanFolder}>
                  {t("resourceSource.dlsite.config.addScanFolder")}
                </Button>
              </div>
              <p className="mb-2 text-xs text-default-400">{t("resourceSource.dlsite.config.scanFoldersTip1")}</p>
              <p className="mb-3 text-xs text-default-400">{t("resourceSource.dlsite.config.scanFoldersTip2")}</p>
              {scanFolders.length === 0 ? (
                <div className="py-3 text-center text-sm text-default-400">{t("resourceSource.dlsite.config.noScanFolders")}</div>
              ) : (
                <div className="space-y-2">
                  {scanFolders.map((folder: string, index: number) => (
                    <div key={index} className="flex items-center gap-2 rounded-lg border-small border-default-200 px-3 py-2">
                      <HeroChip className="max-w-full flex-1" size="sm" variant="flat">
                        {folder}
                      </HeroChip>
                      <Button color="danger" isIconOnly size="sm" variant="light" onPress={() => handleRemoveScanFolder(index)}>
                        <AiOutlineDelete className="text-lg" />
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        ),
      },
      {
        field: DLsiteConfigField.MetadataSync,
        key: "metadata",
        title: t("resourceSource.config.tab.metadataSync"),
        content: <MetadataMappingPanel source={ResourceSource.DLsite} />,
      },
      {
        field: DLsiteConfigField.AutoSync,
        key: "autoSync",
        title: t("thirdPartyConfig.autoSync.tabTitle"),
        content: (
          <AutoSyncPanel
            autoSyncIntervalMinutes={options?.autoSyncIntervalMinutes}
            onSave={(v) => patch({ autoSyncIntervalMinutes: v })}
          />
        ),
      },
      {
        field: DLsiteConfigField.LocaleEmulator,
        key: "le",
        title: t("resourceSource.config.tab.localeEmulator"),
        content: (
          <div className="py-4">
            <LeStatusIndicator />
          </div>
        ),
      },
    ],
    [options, accountFields, scanFolders, downloadDir, t, patch, createPortal],
  );

  return (
    <>
      <ConfigurableThirdPartyPanel fields={fields} tabs={tabs} />
      {showFooter && (
        <div className="mt-4 flex justify-end gap-2 border-t border-default-200 pt-4">
          {onCancel ? (
            <Button variant="light" onPress={onCancel}>
              {t("common.action.cancel")}
            </Button>
          ) : null}
          <Button color="primary" isLoading={saving} onPress={handleSave}>
            {t("common.action.save")}
          </Button>
        </div>
      )}
    </>
  );
};

export interface DLsiteConfigModalProps {
  onDestroyed?: () => void;
  onClose?: () => void;
  isOpen?: boolean;
  fields?: DLsiteConfigField[] | "all";
}

export const DLsiteConfigModal: FC<DLsiteConfigModalProps> = ({ onDestroyed, onClose, isOpen, fields }) => {
  const { t } = useTranslation();
  const handleClose = onClose ?? onDestroyed;
  return (
    <ThirdPartyConfigModal title={t("resourceSource.dlsite.title")} isOpen={isOpen} onClose={handleClose}>
      <DLsiteConfigPanel fields={fields} onCancel={handleClose} />
    </ThirdPartyConfigModal>
  );
};

const DLsiteConfig = DLsiteConfigModal;
export default DLsiteConfig;
