"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Modal, ModalContent, ModalHeader, ModalBody, Tab, Tabs } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useExHentaiOptionsStore } from "@/stores/options";
import { ResourceSource } from "@/sdk/constants";
import AccountsPanel, { type AccountField } from "./AccountsPanel";
import MetadataMappingPanel from "./MetadataMappingPanel";
import AutoSyncPanel from "./AutoSyncPanel";

export enum ExHentaiConfigField {
  Accounts = "accounts",
  MetadataSync = "metadataSync",
  AutoSync = "autoSync",
}

interface ExHentaiConfigProps {
  onDestroyed?: () => void;
  fields?: ExHentaiConfigField[] | "all";
}

export default function ExHentaiConfig({ onDestroyed, fields: visibleFields }: ExHentaiConfigProps) {
  const { t } = useTranslation();
  const options = useExHentaiOptionsStore((s) => s.data);
  const patch = useExHentaiOptionsStore((s) => s.patch);

  const accountFields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
      },
    ],
    [t],
  );

  const handleSave = async (accounts: any[]) => {
    await patch({ accounts });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const isFieldVisible = (field: ExHentaiConfigField) =>
    !visibleFields || visibleFields === "all" || visibleFields.includes(field);

  const tabs = useMemo(() => {
    const allTabs = [
      {
        field: ExHentaiConfigField.Accounts,
        key: "accounts",
        title: t("resourceSource.config.tab.accounts"),
        content: (
          <AccountsPanel
            accounts={options?.accounts || []}
            fields={accountFields}
            onSave={handleSave}
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
    ];
    return allTabs.filter((tab) => isFieldVisible(tab.field));
  }, [visibleFields, options, accountFields, t]);

  return (
    <Modal defaultOpen scrollBehavior="inside" size="5xl" onClose={onDestroyed}>
      <ModalContent>
        <ModalHeader>{t("resourceSource.exhentai.title")}</ModalHeader>
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
