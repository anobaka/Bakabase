"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Switch } from "@/components/bakaui";
import LanguageDropdown from "@/components/LanguageDropdown";
import BApi from "@/sdk/BApi";
import { AiFeature } from "@/sdk/constants";
import type { EnhancerTranslationOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";

type Props = {
  value?: EnhancerTranslationOptions;
  onChange: (value: EnhancerTranslationOptions) => void;
};

const TranslationOptionsSection = ({ value, onChange }: Props) => {
  const { t } = useTranslation();
  const [translationConfigured, setTranslationConfigured] = useState<boolean | null>(null);

  const checkTranslationConfig = useCallback(async () => {
    try {
      const r = await BApi.ai.getAiFeatureConfig(AiFeature.Translation);
      if (!r.code) {
        const cfg = r.data;
        // Translation is configured if there's a config that either has its own provider
        // or uses default (which means default must be configured)
        if (cfg && cfg.providerConfigId) {
          setTranslationConfigured(true);
        } else if (cfg && cfg.useDefault) {
          // Check default config
          const dr = await BApi.ai.getAiFeatureConfig(AiFeature.Default);
          setTranslationConfigured(!!(dr.data && dr.data.providerConfigId));
        } else {
          // No config at all - check if default has config
          const dr = await BApi.ai.getAiFeatureConfig(AiFeature.Default);
          setTranslationConfigured(!!(dr.data && dr.data.providerConfigId));
        }
      }
    } catch {
      setTranslationConfigured(false);
    }
  }, []);

  useEffect(() => {
    checkTranslationConfig();
  }, []);

  const enabled = value?.enabled ?? false;
  const isDisabled = translationConfigured === false;

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <div className="w-[300px]">
          <div className="text-small">
            {t<string>("enhancer.options.translation.label")}
          </div>
          <div className="text-xs text-default-400">
            {t<string>("enhancer.options.translation.description")}
          </div>
        </div>
        <Switch
          isSelected={enabled}
          isDisabled={isDisabled}
          size="sm"
          onValueChange={(v) => {
            onChange({ ...value, enabled: v });
          }}
        />
      </div>
      {isDisabled && (
        <div className="text-xs text-warning-500">
          {t<string>("enhancer.options.translation.notConfigured")}
        </div>
      )}
      {enabled && !isDisabled && (
        <LanguageDropdown
          label={t<string>("enhancer.options.translation.targetLanguage.label")}
          size="sm"
          className="max-w-[200px]"
          value={value?.targetLanguage}
          onValueChange={(v) => {
            onChange({ ...value, enabled: true, targetLanguage: v });
          }}
        />
      )}
    </div>
  );
};

export default TranslationOptionsSection;
