"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Modal, ModalContent, ModalHeader, ModalBody, Tab, Tabs, Select, SelectItem } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useSteamOptionsStore } from "@/stores/options";
import { ResourceSource } from "@/sdk/constants";
import ExternalLink from "@/components/ExternalLink";
import AccountsPanel, { type AccountField } from "./AccountsPanel";
import MetadataMappingPanel from "./MetadataMappingPanel";
import AutoSyncPanel from "./AutoSyncPanel";

const steamLanguageOptions = [
  { value: "english", label: "English" },
  { value: "schinese", label: "简体中文" },
  { value: "tchinese", label: "繁體中文" },
  { value: "japanese", label: "日本語" },
  { value: "korean", label: "한국어" },
  { value: "french", label: "Français" },
  { value: "german", label: "Deutsch" },
  { value: "spanish", label: "Español" },
  { value: "latam", label: "Español (Latinoamérica)" },
  { value: "italian", label: "Italiano" },
  { value: "portuguese", label: "Português" },
  { value: "brazilian", label: "Português (Brasil)" },
  { value: "russian", label: "Русский" },
  { value: "thai", label: "ไทย" },
  { value: "vietnamese", label: "Tiếng Việt" },
];

export enum SteamConfigField {
  General = "general",
  Accounts = "accounts",
  MetadataSync = "metadataSync",
  AutoSync = "autoSync",
}

interface SteamConfigProps {
  onDestroyed?: () => void;
  fields?: SteamConfigField[] | "all";
}

export default function SteamConfig({ onDestroyed, fields: visibleFields }: SteamConfigProps) {
  const { t } = useTranslation();
  const steamOptions = useSteamOptionsStore((s) => s.data);
  const patch = useSteamOptionsStore((s) => s.patch);

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "apiKey",
        label: t("resourceSource.accounts.apiKey"),
        placeholder: t("resourceSource.accounts.apiKeyPlaceholder"),
        type: "password" as const,
        description: (
          <ExternalLink href="https://steamcommunity.com/dev/apikey" size="sm">
            {t("resourceSource.steam.config.getApiKey")}
          </ExternalLink>
        ),
      },
      {
        key: "steamId",
        label: t("resourceSource.accounts.steamId"),
        placeholder: t("resourceSource.accounts.steamIdPlaceholder"),
      },
    ],
    [t],
  );

  const handleSave = async (accounts: any[]) => {
    await patch({ accounts });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const isFieldVisible = (field: SteamConfigField) =>
    !visibleFields || visibleFields === "all" || visibleFields.includes(field);

  const tabs = useMemo(() => {
    const allTabs = [
      {
        field: SteamConfigField.General,
        key: "general",
        title: t("resourceSource.config.tab.general"),
        content: (
          <Select
            label={t("thirdPartyConfig.steam.language.label")}
            description={t("thirdPartyConfig.steam.language.description")}
            placeholder={t("thirdPartyConfig.steam.language.auto")}
            selectedKeys={steamOptions?.language ? [steamOptions.language] : []}
            onSelectionChange={async (keys) => {
              const selected = Array.from(keys)[0] as string | undefined;
              await patch({ language: selected ?? null });
              toast.success(t("thirdPartyConfig.success.saved"));
            }}
            className="max-w-xs"
          >
            {steamLanguageOptions.map((opt) => (
              <SelectItem key={opt.value}>{opt.label}</SelectItem>
            ))}
          </Select>
        ),
      },
      {
        field: SteamConfigField.Accounts,
        key: "accounts",
        title: t("resourceSource.config.tab.accounts"),
        content: (
          <AccountsPanel
            accounts={steamOptions?.accounts || []}
            fields={accountFields}
            onSave={handleSave}
          />
        ),
      },
      {
        field: SteamConfigField.MetadataSync,
        key: "metadata",
        title: t("resourceSource.config.tab.metadataSync"),
        content: <MetadataMappingPanel source={ResourceSource.Steam} />,
      },
      {
        field: SteamConfigField.AutoSync,
        key: "autoSync",
        title: t("thirdPartyConfig.autoSync.tabTitle"),
        content: (
          <AutoSyncPanel
            autoSyncIntervalMinutes={steamOptions?.autoSyncIntervalMinutes}
            onSave={(v) => patch({ autoSyncIntervalMinutes: v })}
          />
        ),
      },
    ];
    return allTabs.filter((tab) => isFieldVisible(tab.field));
  }, [visibleFields, steamOptions, accountFields, t]);

  return (
    <Modal defaultOpen scrollBehavior="inside" size="5xl" onClose={onDestroyed}>
      <ModalContent>
        <ModalHeader>{t("resourceSource.steam.title")}</ModalHeader>
        <ModalBody className="pb-6">
          {tabs.length === 1 ? (
            tabs[0].content
          ) : (
            <Tabs isVertical classNames={{ panel: "flex-1 w-0" }}>
              {tabs.map((tab) => (
                <Tab key={tab.key} title={tab.title}>
                  {tab.content}
                </Tab>
              ))}
            </Tabs>
          )}
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}
