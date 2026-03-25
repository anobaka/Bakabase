"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";

import { toast } from "@/components/bakaui";
import { useSteamOptionsStore } from "@/stores/options";
import { ResourceSource } from "@/sdk/constants";
import AccountsConfigModal, {
  type AccountField,
} from "./AccountsConfigModal";
import MetadataMappingConfig from "./MetadataMappingConfig";

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
    <AccountsConfigModal
      accounts={steamOptions?.accounts || []}
      extraContent={<MetadataMappingConfig source={ResourceSource.Steam} />}
      fields={fields}
      isOpen={isOpen}
      platform="Steam"
      onClose={onClose}
      onSave={handleSave}
    />
  );
}
