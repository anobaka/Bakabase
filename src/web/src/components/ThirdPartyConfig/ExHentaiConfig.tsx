"use client";

import { useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, ModalContent, ModalHeader, ModalBody, ModalFooter, Tab, Tabs } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useExHentaiOptionsStore } from "@/stores/options";
import { CookieValidatorTarget, ResourceSource } from "@/sdk/constants";
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
  onClose?: () => void;
  isOpen?: boolean;
  fields?: ExHentaiConfigField[] | "all";
}

export default function ExHentaiConfig({ onDestroyed, onClose, isOpen, fields: visibleFields }: ExHentaiConfigProps) {
  const { t } = useTranslation();
  const options = useExHentaiOptionsStore((s) => s.data);
  const patch = useExHentaiOptionsStore((s) => s.patch);
  const [saving, setSaving] = useState(false);
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

  const handleClose = onClose ?? onDestroyed;

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
      handleClose?.();
    } finally {
      setSaving(false);
    }
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
            hideFooter
            onAccountsChange={(accs) => { pendingAccountsRef.current = accs; }}
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
    ];
    return allTabs.filter((tab) => isFieldVisible(tab.field));
  }, [visibleFields, options, accountFields, t]);

  return (
    <Modal isOpen={isOpen ?? true} scrollBehavior="inside" size="5xl" onClose={handleClose}>
      <ModalContent>
        <ModalHeader>{t("resourceSource.exhentai.title")}</ModalHeader>
        <ModalBody>
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
        <ModalFooter>
          <Button variant="light" onPress={handleClose}>
            {t("common.action.cancel")}
          </Button>
          <Button color="primary" isLoading={saving} onPress={handleSave}>
            {t("common.action.save")}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}
