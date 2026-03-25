"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";

import { FiExternalLink } from "react-icons/fi";
import { Link } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useSteamOptionsStore } from "@/stores/options";
import AccountsConfigModal, {
  type AccountField,
} from "./AccountsConfigModal";

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
    <AccountsConfigModal
      accounts={steamOptions?.accounts || []}
      fields={fields}
      isOpen={isOpen}
      platform="Steam"
      onClose={onClose}
      onSave={handleSave}
    />
  );
}
