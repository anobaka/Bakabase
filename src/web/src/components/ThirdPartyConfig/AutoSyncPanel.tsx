"use client";

import { useTranslation } from "react-i18next";
import { Input } from "@heroui/react";

interface AutoSyncPanelProps {
  autoSyncIntervalMinutes?: number | null;
  onSave: (intervalMinutes: number | null) => void;
}

export default function AutoSyncPanel({ autoSyncIntervalMinutes, onSave }: AutoSyncPanelProps) {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      <Input
        type="number"
        label={t("thirdPartyConfig.autoSync.intervalLabel")}
        description={t("thirdPartyConfig.autoSync.intervalDescription")}
        placeholder="0"
        min={0}
        value={autoSyncIntervalMinutes?.toString() ?? ""}
        onValueChange={(v) => {
          const num = parseInt(v, 10);
          onSave(isNaN(num) || num <= 0 ? null : num);
        }}
        endContent={<span className="text-default-400 text-sm">{t("thirdPartyConfig.autoSync.unit")}</span>}
        className="max-w-xs"
      />
      <p className="text-xs text-warning">
        {t("thirdPartyConfig.autoSync.warning")}
      </p>
    </div>
  );
}
