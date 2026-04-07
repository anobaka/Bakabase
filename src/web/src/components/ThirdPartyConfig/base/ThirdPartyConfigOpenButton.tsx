"use client";

import { useState, type ComponentType } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@heroui/react";

type ConfigProps = {
  isOpen?: boolean;
  onClose?: () => void;
  onDestroyed?: () => void;
};

type ConfigComponent = ComponentType<ConfigProps>;

interface ThirdPartyConfigOpenButtonProps {
  Config: ConfigComponent;
  /** When set, button shows "(n accounts)" suffix. Omit for platforms without accounts (e.g. TMDB). */
  accountCount?: number;
}

/**
 * Opens a third-party Config modal (SteamConfig-style) from the third-party settings page.
 */
export default function ThirdPartyConfigOpenButton({ Config, accountCount }: ThirdPartyConfigOpenButtonProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <div className="flex items-center gap-2">
      <Button size="sm" variant="flat" onPress={() => setOpen(true)}>
        {accountCount === undefined
          ? t("resourceSource.action.configure")
          : `${t("resourceSource.action.configure")} (${accountCount} ${t("resourceSource.accounts.title", { platform: "" }).trim()})`}
      </Button>
      {open && <Config isOpen onClose={() => setOpen(false)} />}
    </div>
  );
}
