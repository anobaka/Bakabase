"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Modal, ModalContent, ModalHeader, ModalBody, Tab, Tabs } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useExHentaiOptionsStore } from "@/stores/options";
import { ResourceSource } from "@/sdk/constants";
import AccountsPanel, { type AccountField } from "./AccountsPanel";
import MetadataMappingPanel from "./MetadataMappingPanel";

interface ExHentaiConfigProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function ExHentaiConfig({ isOpen, onClose }: ExHentaiConfigProps) {
  const { t } = useTranslation();
  const options = useExHentaiOptionsStore((s) => s.data);
  const patch = useExHentaiOptionsStore((s) => s.patch);

  const fields: AccountField[] = useMemo(
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

  return (
    <Modal isOpen={isOpen} scrollBehavior="inside" size="5xl" onClose={onClose}>
      <ModalContent>
        <ModalHeader>{t("resourceSource.exhentai.title")}</ModalHeader>
        <ModalBody className="pb-6">
          <Tabs isVertical variant="underlined">
            <Tab key="accounts" title={t("resourceSource.config.tab.accounts")}>
              <AccountsPanel
                accounts={options?.accounts || []}
                fields={fields}
                onSave={handleSave}
              />
            </Tab>
            <Tab key="metadata" title={t("resourceSource.config.tab.metadataSync")}>
              <MetadataMappingPanel source={ResourceSource.ExHentai} />
            </Tab>
          </Tabs>
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}
