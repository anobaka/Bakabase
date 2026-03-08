"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input, Switch } from "@heroui/react";

import { toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type { BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions } from "@/sdk/Api";

type AiOptions = BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions;

const QuotaSettings = () => {
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
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <Switch
          size="sm"
          isSelected={options.auditLogRequestContent}
          onValueChange={(v) => patch({ auditLogRequestContent: v })}
        />
        <div className="flex flex-col">
          <span className="text-sm">{t("configuration.ai.quota.enableAuditContent")}</span>
          <span className="text-xs text-default-400">{t("configuration.ai.quota.enableAuditContentTip")}</span>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Input
          size="sm"
          type="number"
          label={t<string>("configuration.ai.quota.dailyLimit")}
          placeholder={t<string>("configuration.ai.quota.noLimit")}
          value={options.quota?.dailyTokenLimit?.toString() ?? ""}
          onValueChange={(v) => {
            const val = v ? parseInt(v, 10) : undefined;
            patch({ quota: { ...options.quota, dailyTokenLimit: val } });
          }}
        />
        <Input
          size="sm"
          type="number"
          label={t<string>("configuration.ai.quota.monthlyLimit")}
          placeholder={t<string>("configuration.ai.quota.noLimit")}
          value={options.quota?.monthlyTokenLimit?.toString() ?? ""}
          onValueChange={(v) => {
            const val = v ? parseInt(v, 10) : undefined;
            patch({ quota: { ...options.quota, monthlyTokenLimit: val } });
          }}
        />
      </div>
    </div>
  );
};

export default QuotaSettings;
