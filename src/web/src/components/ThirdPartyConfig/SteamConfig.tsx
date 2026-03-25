"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Modal, ModalContent, ModalHeader, ModalBody, Tab, Tabs } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useSteamOptionsStore } from "@/stores/options";
import { ResourceSource } from "@/sdk/constants";
import ExternalLink from "@/components/ExternalLink";
import AccountsPanel, { type AccountField } from "./AccountsPanel";
import MetadataMappingPanel from "./MetadataMappingPanel";

interface SteamConfigProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function SteamConfig({ isOpen, onClose }: SteamConfigProps) {
  const { t } = useTranslation();
  const steamOptions = useSteamOptionsStore((s) => s.data);
  const patch = useSteamOptionsStore((s) => s.patch);

  const fields: AccountField[] = useMemo(
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

  return (
    <Modal isOpen={isOpen} scrollBehavior="inside" size="5xl" onOpenChange={(open) => { if (!open) onClose(); }}>
      <ModalContent>
        <ModalHeader>{t("resourceSource.steam.title")}</ModalHeader>
        <ModalBody className="pb-6">
          <Tabs isVertical classNames={{ panel: "flex-1 w-0" }}>
            <Tab key="accounts" title={t("resourceSource.config.tab.accounts")}>
              <AccountsPanel
                accounts={steamOptions?.accounts || []}
                fields={fields}
                onSave={handleSave}
              />
            </Tab>
            <Tab key="metadata" title={t("resourceSource.config.tab.metadataSync")}>
              <MetadataMappingPanel source={ResourceSource.Steam} />
            </Tab>
          </Tabs>
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}
