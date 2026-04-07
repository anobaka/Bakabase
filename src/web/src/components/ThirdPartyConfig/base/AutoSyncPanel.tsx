"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input } from "@heroui/react";
import { toast } from "@/components/bakaui";

interface AutoSyncPanelProps {
  autoSyncIntervalMinutes?: number | null;
  onSave: (intervalMinutes: number | null) => Promise<void> | void;
}

export default function AutoSyncPanel({ autoSyncIntervalMinutes, onSave }: AutoSyncPanelProps) {
  const { t } = useTranslation();
  const [value, setValue] = useState(autoSyncIntervalMinutes?.toString() ?? "");
  const debounceRef = useRef<ReturnType<typeof setTimeout>>();
  const lastSavedRef = useRef<number | null | undefined>(autoSyncIntervalMinutes);

  // Sync from prop when it changes externally
  useEffect(() => {
    setValue(autoSyncIntervalMinutes?.toString() ?? "");
    lastSavedRef.current = autoSyncIntervalMinutes;
  }, [autoSyncIntervalMinutes]);

  const parseValue = (v: string): number | null => {
    const num = parseInt(v, 10);
    return isNaN(num) || num <= 0 ? null : num;
  };

  const save = (v: string) => {
    const result = parseValue(v);
    if (result === lastSavedRef.current) return;
    lastSavedRef.current = result;
    Promise.resolve(onSave(result)).then(() => {
      toast.success(t("thirdPartyConfig.success.saved"));
    });
  };

  const handleChange = (v: string) => {
    setValue(v);
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => save(v), 800);
  };

  const handleBlur = () => {
    clearTimeout(debounceRef.current);
    save(value);
  };

  // Cleanup debounce on unmount
  useEffect(() => () => clearTimeout(debounceRef.current), []);

  return (
    <div className="flex flex-col gap-4">
      <Input
        type="number"
        label={t("thirdPartyConfig.autoSync.intervalLabel")}
        description={t("thirdPartyConfig.autoSync.intervalDescription")}
        placeholder="0"
        min={0}
        value={value}
        onValueChange={handleChange}
        onBlur={handleBlur}
        endContent={<span className="text-default-400 text-sm whitespace-nowrap">{t("thirdPartyConfig.autoSync.unit")}</span>}
        className="max-w-xs"
      />
      <p className="text-xs text-warning">
        {t("thirdPartyConfig.autoSync.warning")}
      </p>
    </div>
  );
}
