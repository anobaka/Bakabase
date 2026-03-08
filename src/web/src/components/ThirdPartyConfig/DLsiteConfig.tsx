"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";

import { toast } from "@/components/bakaui";
import { useDLsiteOptionsStore } from "@/stores/options";
import AccountsConfigModal, {
  type AccountField,
} from "./AccountsConfigModal";

interface DLsiteConfigProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function DLsiteConfig({ isOpen, onClose }: DLsiteConfigProps) {
  const { t } = useTranslation();
  const options = useDLsiteOptionsStore((s) => s.data);
  const patch = useDLsiteOptionsStore((s) => s.patch);

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
    <AccountsConfigModal
      accounts={options?.accounts || []}
      fields={fields}
      isOpen={isOpen}
      platform="DLsite"
      onClose={onClose}
      onSave={handleSave}
    />
  );
}
