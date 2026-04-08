"use client";

import { useEffect, useMemo, useRef, useState, type FC } from "react";
import { useTranslation } from "react-i18next";
import { Button, Textarea } from "@heroui/react";

import { Chip, NumberInput, toast } from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import BApi from "@/sdk/BApi";
import { useExHentaiOptionsStore } from "@/stores/options";
import { CookieValidatorTarget, ResourceSource } from "@/sdk/constants";
import AccountsPanel, { type AccountField } from "../base/AccountsPanel";
import ConfigurableThirdPartyPanel, { type ConfigFieldTab } from "../base/ConfigurableThirdPartyPanel";
import MetadataMappingPanel from "../base/MetadataMappingPanel";
import TampermonkeyInstallButton from "../base/TampermonkeyInstallButton";
import AutoSyncPanel from "../base/AutoSyncPanel";
import ThirdPartyConfigModal from "../base/ThirdPartyConfigModal";

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
  /** Show Save (and optional Cancel). Default true. */
  showFooter?: boolean;
  onCancel?: () => void;
}

export const ExHentaiConfigPanel: FC<ExHentaiConfigPanelProps> = ({
  fields = "all",
  showFooter = true,
  onCancel,
}) => {
  const { t } = useTranslation();
  const options = useExHentaiOptionsStore((s) => s.data);
  const patch = useExHentaiOptionsStore((s) => s.patch);
  const [saving, setSaving] = useState(false);
  const [namingDefinition, setNamingDefinition] = useState<any>();

  const showDownload = fields === "all" || (Array.isArray(fields) && fields.includes(ExHentaiConfigField.Download));
  useEffect(() => {
    if (!showDownload) return;
    BApi.downloadTask.getAllDownloaderDefinitions().then((res) => {
      const def = (res.data || []).find((d) => d.thirdPartyId === 2); // ExHentai = 2
      if (def) setNamingDefinition(def);
    });
  }, [showDownload]);
  const pendingAccountsRef = useRef<any[] | null>(null);

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
            hideFooter
            onAccountsChange={(accs) => {
              pendingAccountsRef.current = accs;
            }}
            onSave={async () => {}}
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
            onSave={(v) => patch({ autoSyncIntervalMinutes: v })}
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
              label={t<string>("thirdPartyConfig.label.maxConcurrency")}
              description={t<string>("thirdPartyConfig.field.maxConcurrency.description")}
              min={1} max={100}
              value={options?.maxConcurrency || 1}
              onValueChange={(v) => patch({ maxConcurrency: v })}
            />
            <NumberInput
              label={t<string>("thirdPartyConfig.label.requestInterval")}
              description={t<string>("thirdPartyConfig.field.requestInterval.description")}
              min={0}
              value={options?.requestInterval || 1000}
              onValueChange={(v) => patch({ requestInterval: v })}
            />
            <NumberInput
              label={t<string>("thirdPartyConfig.label.maxRetries")}
              description={t<string>("thirdPartyConfig.field.maxRetries.description")}
              min={0}
              value={options?.maxRetries || 0}
              onValueChange={(v) => patch({ maxRetries: v })}
            />
            <NumberInput
              label={t<string>("thirdPartyConfig.label.requestTimeout")}
              description={t<string>("thirdPartyConfig.field.requestTimeout.description")}
              min={0}
              value={options?.requestTimeout || 0}
              onValueChange={(v) => patch({ requestTimeout: v })}
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
            <div>
              <span className="text-sm font-medium">{t<string>("thirdPartyConfig.field.defaultPath.label")}</span>
              <div className="mt-1">
                <FileSystemSelectorButton
                  fileSystemSelectorProps={{
                    targetType: "folder",
                    onSelected: (e) => patch({ defaultPath: e.path }),
                    defaultSelectedPath: options?.defaultPath,
                  }}
                />
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
    [options, accountFields, t, patch],
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
      title={t("resourceSource.exhentai.title")}
      isOpen={isOpen}
      onClose={handleClose}
    >
      <ExHentaiConfigPanel fields={fields} onCancel={handleClose} />
    </ThirdPartyConfigModal>
  );
};

const ExHentaiConfig = ExHentaiConfigModal;
export default ExHentaiConfig;
