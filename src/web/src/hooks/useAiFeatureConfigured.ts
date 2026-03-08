import { useCallback, useEffect, useState } from "react";

import BApi from "@/sdk/BApi";
import { AiFeature } from "@/sdk/constants";

/**
 * Checks whether an AI feature has a valid provider + model configured.
 * Resolves through the useDefault chain automatically.
 */
export default function useAiFeatureConfigured(feature: AiFeature) {
  const [isConfigured, setIsConfigured] = useState(false);
  const [isLoading, setIsLoading] = useState(true);

  const check = useCallback(async () => {
    setIsLoading(true);
    try {
      const isDefault = feature === AiFeature.Default;
      const featureRes = await BApi.ai.getAiFeatureConfig(feature);
      const featureCfg = featureRes.code ? null : (featureRes.data ?? null);

      let displayCfg = featureCfg;

      if (!isDefault && (!featureCfg || featureCfg.useDefault)) {
        const defaultRes = await BApi.ai.getAiFeatureConfig(AiFeature.Default);
        displayCfg = defaultRes.code ? null : (defaultRes.data ?? null);
      }

      setIsConfigured(
        displayCfg != null &&
        displayCfg.providerConfigId != null &&
        displayCfg.modelId != null &&
        displayCfg.modelId !== "",
      );
    } finally {
      setIsLoading(false);
    }
  }, [feature]);

  useEffect(() => {
    check();
  }, [check]);

  return { isConfigured, isLoading, recheck: check };
}
