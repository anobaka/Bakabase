"use client";

import { useTranslation } from "react-i18next";

import { Tooltip } from "@/components/bakaui";

interface ScoringExplanationProps {
  /** Show as tooltip trigger (icon) or inline text */
  mode?: "tooltip" | "inline";
  /** Custom className */
  className?: string;
}

const ScoringExplanation = ({ mode = "tooltip", className }: ScoringExplanationProps) => {
  const { t } = useTranslation();

  const content = (
    <div className="flex flex-col gap-1 text-xs max-w-md">
      <div className="font-medium text-sm">{t("comparison.scoring.title")}</div>
      <div className="font-mono text-primary">{t("comparison.scoring.formula")}</div>
      <ul className="list-disc list-inside space-y-1 text-default-500">
        <li>{t("comparison.scoring.ruleScore")}</li>
        <li>{t("comparison.scoring.weightedSum")}</li>
        <li>{t("comparison.scoring.threshold")}</li>
      </ul>
      <div className="text-default-400 italic mt-1">{t("comparison.scoring.example")}</div>
    </div>
  );

  if (mode === "inline") {
    return <div className={className}>{content}</div>;
  }

  return (
    <Tooltip content={content}>
      <span className={`text-default-400 cursor-help ${className || ""}`}>ⓘ</span>
    </Tooltip>
  );
};

ScoringExplanation.displayName = "ScoringExplanation";

export default ScoringExplanation;
