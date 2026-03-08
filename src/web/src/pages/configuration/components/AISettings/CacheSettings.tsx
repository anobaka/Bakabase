"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input, Switch } from "@heroui/react";

import { toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type { BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions } from "@/sdk/Api";

type AiOptions = BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions;

const CacheSettings = () => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<AiOptions | null>(null);

  const load = useCallback(async () => {
    const r = await BApi.options.getAiOptions();
    if (!r.code && r.data) {
      setOptions(r.data);
    }
  }, []);

  useEffect(() => {
    load();
  }, []);

  const patch = async (patch: Record<string, unknown>) => {
    const r = await BApi.options.patchAiOptions(patch);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      await load();
    }
  };

  if (!options) return null;

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <Switch
          size="sm"
          isSelected={options.enableCache}
          onValueChange={(v) => patch({ enableCache: v })}
        />
        <span className="text-sm">{t("configuration.ai.cache.enableCache")}</span>
      </div>
      <Input
        size="sm"
        type="number"
        label={t<string>("configuration.ai.cache.ttl")}
        className="max-w-[180px]"
        value={String(options.defaultCacheTtlDays)}
        onValueChange={(v) => {
          const val = parseInt(v, 10);
          if (!isNaN(val) && val > 0) {
            patch({ defaultCacheTtlDays: val });
          }
        }}
      />
    </div>
  );
};

export default CacheSettings;
