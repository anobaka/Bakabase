"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Chip } from "@heroui/react";

import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIComponentsObservationLlmUsageSummary,
  BakabaseModulesAIModelsDbLlmProviderConfigDbModel,
} from "@/sdk/Api";
import { LlmProviderTypeLabel } from "@/sdk/constants";

type UsageSummary = BakabaseModulesAIComponentsObservationLlmUsageSummary;
type LlmProviderConfig = BakabaseModulesAIModelsDbLlmProviderConfigDbModel;

// Feature string (from backend) → i18n key mapping
const featureI18nKey: Record<string, string> = {
  Default: "configuration.ai.feature.Default",
  Enhancer: "configuration.ai.feature.Enhancer",
  Translation: "configuration.ai.feature.Translation",
  FileProcessor: "configuration.ai.feature.FileProcessor",
};

const AiUsagePage = () => {
  const { t } = useTranslation();
  const [summary, setSummary] = useState<UsageSummary | null>(null);
  const [providers, setProviders] = useState<LlmProviderConfig[]>([]);

  const load = useCallback(async () => {
    const [summaryRes, providersRes] = await Promise.all([
      BApi.ai.getLlmUsageSummary(),
      BApi.ai.getAllLlmProviders(),
    ]);
    if (!summaryRes.code && summaryRes.data) {
      setSummary(summaryRes.data);
    }
    if (!providersRes.code && providersRes.data) {
      setProviders(providersRes.data);
    }
  }, []);

  useEffect(() => {
    load();
  }, []);

  const getProviderLabel = (providerConfigId: number): string => {
    const p = providers.find((pr) => pr.id === providerConfigId);
    if (!p) return `#${providerConfigId}`;
    const typeName = LlmProviderTypeLabel[p.providerType] ?? "";
    return `${p.name}${typeName ? ` (${typeName})` : ""}`;
  };

  const getFeatureLabel = (feature: string): string => {
    const key = featureI18nKey[feature];
    return key ? t<string>(key) : feature;
  };

  if (!summary || summary.totalCalls === 0) {
    return (
      <div className="p-4 text-default-400">
        {t("configuration.ai.usage.noData")}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6 p-4">
      {/* Summary cards */}
      <div className="grid grid-cols-3 gap-4">
        <div className="flex flex-col gap-1 p-4 rounded-lg bg-default-100">
          <span className="text-xs text-default-400">{t("configuration.ai.usage.todayTokens")}</span>
          <span className="text-2xl font-semibold">{summary.todayTokens.toLocaleString()}</span>
        </div>
        <div className="flex flex-col gap-1 p-4 rounded-lg bg-default-100">
          <span className="text-xs text-default-400">{t("configuration.ai.usage.monthTokens")}</span>
          <span className="text-2xl font-semibold">{summary.monthTokens.toLocaleString()}</span>
        </div>
        <div className="flex flex-col gap-1 p-4 rounded-lg bg-default-100">
          <span className="text-xs text-default-400">{t("configuration.ai.usage.totalTokens")}</span>
          <span className="text-2xl font-semibold">{summary.totalTokens.toLocaleString()}</span>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-4">
        <div className="flex flex-col gap-1 p-4 rounded-lg bg-default-100">
          <span className="text-xs text-default-400">{t("configuration.ai.usage.totalCalls")}</span>
          <span className="text-2xl font-semibold">{summary.totalCalls.toLocaleString()}</span>
        </div>
        <div className="flex flex-col gap-1 p-4 rounded-lg bg-default-100">
          <span className="text-xs text-default-400">{t("configuration.ai.usage.cacheHits")}</span>
          <span className="text-2xl font-semibold">{summary.cacheHits.toLocaleString()}</span>
        </div>
        <div className="flex flex-col gap-1 p-4 rounded-lg bg-default-100">
          <span className="text-xs text-default-400">{t("configuration.ai.usage.cacheHitRate")}</span>
          <span className="text-2xl font-semibold">{(summary.cacheHitRate * 100).toFixed(1)}%</span>
        </div>
      </div>

      {/* By provider */}
      {summary.byProvider.length > 0 && (
        <div className="flex flex-col gap-3">
          <span className="text-sm font-medium">{t("configuration.ai.usage.byProvider")}</span>
          <div className="grid grid-cols-2 gap-3">
            {summary.byProvider.map((p) => (
              <div key={`${p.providerConfigId}-${p.modelId}`} className="flex items-center justify-between p-3 rounded-lg bg-default-50 border border-default-200">
                <div className="flex flex-col gap-0.5">
                  <span className="text-sm">{getProviderLabel(p.providerConfigId)}</span>
                  <span className="text-xs text-default-400 font-mono">{p.modelId}</span>
                </div>
                <div className="flex items-center gap-2">
                  <Chip size="sm" variant="flat">{p.totalTokens.toLocaleString()} tokens</Chip>
                  <Chip size="sm" variant="flat" color="primary">{t("configuration.ai.usage.calls", { count: p.callCount })}</Chip>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* By feature */}
      {summary.byFeature.length > 0 && (
        <div className="flex flex-col gap-3">
          <span className="text-sm font-medium">{t("configuration.ai.usage.byFeature")}</span>
          <div className="grid grid-cols-2 gap-3">
            {summary.byFeature.map((f) => (
              <div key={f.feature} className="flex items-center justify-between p-3 rounded-lg bg-default-50 border border-default-200">
                <span className="text-sm">{getFeatureLabel(f.feature)}</span>
                <div className="flex items-center gap-2">
                  <Chip size="sm" variant="flat">{f.totalTokens.toLocaleString()} tokens</Chip>
                  <Chip size="sm" variant="flat" color="primary">{t("configuration.ai.usage.calls", { count: f.callCount })}</Chip>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};

AiUsagePage.displayName = "AiUsagePage";

export default AiUsagePage;
