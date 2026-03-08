"use client";

import type { ComponentProps } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";
import type { AiFeature } from "@/sdk/constants";
import useAiFeatureConfigured from "@/hooks/useAiFeatureConfigured";

type Props = ComponentProps<typeof Button> & {
  /** The AI feature whose configuration is checked. */
  feature: AiFeature;
};

/**
 * A Button that automatically disables itself when the given AI feature
 * has no valid provider/model configured, replacing children with a
 * "configure first" message.
 */
const AiFeatureButton = ({ feature, isDisabled, children, ...rest }: Props) => {
  const { t } = useTranslation();
  const { isConfigured, isLoading } = useAiFeatureConfigured(feature);

  const needsConfig = !isLoading && !isConfigured;
  const shouldDisable = isDisabled || isLoading || needsConfig;

  return (
    <Button {...rest} isDisabled={shouldDisable}>
      {needsConfig ? t("ai.button.configureFirst") : children}
    </Button>
  );
};

AiFeatureButton.displayName = "AiFeatureButton";

export default AiFeatureButton;
