"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Modal, ModalContent, ModalHeader, ModalBody, Tab, Tabs } from "@heroui/react";
import { FiExternalLink } from "react-icons/fi";
import { Link } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useSteamOptionsStore } from "@/stores/options";
import { ResourceSource } from "@/sdk/constants";
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
          <Link
            className="text-xs gap-1"
            href="https://steamcommunity.com/dev/apikey"
            isExternal
            showAnchorIcon
            anchorIcon={<FiExternalLink />}
            size="sm"
          >
            {t("resourceSource.steam.config.getApiKey")}
          </Link>
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
    <Modal isOpen={isOpen} scrollBehavior="inside" size="5xl" onClose={onClose}>
      <ModalContent>
        <ModalHeader>{t("resourceSource.steam.title")}</ModalHeader>
        <ModalBody className="pb-6">
          <Tabs variant="underlined">
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
