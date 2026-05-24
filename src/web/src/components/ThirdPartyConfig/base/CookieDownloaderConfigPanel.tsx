"use client";

import type { BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition } from "@/sdk/Api";
import type { CookieValidatorTarget, ThirdPartyId } from "@/sdk/constants";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Textarea } from "@heroui/react";

import AccountsPanel, { type AccountField } from "./AccountsPanel";
import ConfigurableThirdPartyPanel, { type ConfigFieldTab } from "./ConfigurableThirdPartyPanel";

import { Chip, NumberInput, toast } from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import BApi from "@/sdk/BApi";

export enum CookieDownloaderConfigField {
  Accounts = "accounts",
  DataFetch = "dataFetch",
  Download = "download",
}

export interface CookieDownloaderConfigPanelProps {
  title: string;
  thirdPartyId: ThirdPartyId;
  fields?: CookieDownloaderConfigField[] | "all";
  options: any;
  patch: (p: any) => Promise<void>;
  patchApi: (p: any) => Promise<any>;
  cookieValidatorTarget: CookieValidatorTarget;
  cookieCaptureTarget: CookieValidatorTarget;
}

export default function CookieDownloaderConfigPanel({
  fields = "all",
  thirdPartyId,
  options,
  patch,
  patchApi,
  cookieValidatorTarget,
  cookieCaptureTarget,
}: CookieDownloaderConfigPanelProps) {
  const { t } = useTranslation();
  const [namingDefinition, setNamingDefinition] = useState<
    | BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition
    | undefined
  >();

  const showDownload = fields === "all" || fields.includes(CookieDownloaderConfigField.Download);

  // Load naming definition from downloader API when Download group is visible
  useEffect(() => {
    if (!showDownload) return;
    BApi.downloadTask.getAllDownloaderDefinitions().then((res) => {
      const def = (res.data || []).find((d) => d.thirdPartyId === thirdPartyId);

      if (def) setNamingDefinition(def);
    });
  }, [showDownload, thirdPartyId]);

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
        cookieValidatorTarget,
        cookieCaptureTarget,
      },
    ],
    [t, cookieValidatorTarget, cookieCaptureTarget],
  );

  const handleAccountsSave = async (accounts: any[]) => {
    await patch({ accounts });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const tabs: ConfigFieldTab<CookieDownloaderConfigField>[] = useMemo(
    () => [
      {
        field: CookieDownloaderConfigField.Accounts,
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
        field: CookieDownloaderConfigField.DataFetch,
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
              onValueChange={(v) => patch({ maxConcurrency: v })}
            />
            <NumberInput
              description={t<string>("thirdPartyConfig.field.requestInterval.description")}
              label={t<string>("thirdPartyConfig.label.requestInterval")}
              min={0}
              value={options?.requestInterval || 1000}
              onValueChange={(v) => patch({ requestInterval: v })}
            />
            <NumberInput
              description={t<string>("thirdPartyConfig.field.maxRetries.description")}
              label={t<string>("thirdPartyConfig.label.maxRetries")}
              min={0}
              value={options?.maxRetries || 0}
              onValueChange={(v) => patch({ maxRetries: v })}
            />
            <NumberInput
              description={t<string>("thirdPartyConfig.field.requestTimeout.description")}
              label={t<string>("thirdPartyConfig.label.requestTimeout")}
              min={0}
              value={options?.requestTimeout || 0}
              onValueChange={(v) => patch({ requestTimeout: v })}
            />
          </div>
        ),
      },
      {
        field: CookieDownloaderConfigField.Download,
        key: "download",
        title: t("thirdPartyConfig.group.download"),
        content: (
          <div className="space-y-4">
            <div>
              <span className="text-sm font-medium">
                {t<string>("thirdPartyConfig.field.defaultPath.label")}
              </span>
              <div className="mt-1">
                <FileSystemSelectorButton
                  fileSystemSelectorProps={{
                    targetType: "folder",
                    onSelected: (e) => patch({ defaultPath: e.path }),
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
                            patch({
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
              onValueChange={(v) => patch({ namingConvention: v })}
            />
          </div>
        ),
      },
    ],
    [
      t,
      options,
      accountFields,
      patchApi,
      cookieValidatorTarget,
      cookieCaptureTarget,
      namingDefinition,
    ],
  );

  return <ConfigurableThirdPartyPanel fields={fields} tabs={tabs} />;
}
