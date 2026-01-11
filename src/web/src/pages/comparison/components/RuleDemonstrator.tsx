"use client";

import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";

import { Chip, Tooltip } from "@/components/bakaui";
import { ComparisonMode, NullValueBehavior } from "@/sdk/constants";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import { AllComparisonModes, NullValueBehaviors } from "./RuleConfigPanel";
import type { ModeParameter, FixedToleranceParameter, RelativeToleranceParameter, TimeWindowParameter, RegexExtractNumberParameter } from "./RuleConfigPanel";

export interface RuleDemonstratorProps {
  property?: IProperty;
  propertyId?: number;
  mode: ComparisonMode;
  parameter?: ModeParameter;
  normalize?: boolean;
  weight: number;
  isVeto: boolean;
  vetoThreshold?: number;
  oneNullBehavior: NullValueBehavior;
  bothNullBehavior: NullValueBehavior;
  onClick?: () => void;
  onDelete?: () => void;
}

const RuleDemonstrator = ({
  property,
  propertyId,
  mode,
  parameter,
  normalize,
  weight,
  isVeto,
  vetoThreshold,
  oneNullBehavior,
  bothNullBehavior,
  onClick,
  onDelete,
}: RuleDemonstratorProps) => {
  const { t } = useTranslation();

  const modeLabel = AllComparisonModes.find((m) => m.value === mode)?.label || "Unknown";
  const oneNullLabel = NullValueBehaviors.find((b) => b.value === oneNullBehavior)?.label || "Unknown";
  const bothNullLabel = NullValueBehaviors.find((b) => b.value === bothNullBehavior)?.label || "Unknown";

  const getParameterDisplay = () => {
    if (!parameter) return null;
    if (mode === ComparisonMode.FixedTolerance && "tolerance" in parameter) {
      return `${t("comparison.label.tolerance")}: ${(parameter as FixedToleranceParameter).tolerance}`;
    }
    if (mode === ComparisonMode.RelativeTolerance && "tolerancePercent" in parameter) {
      return `${t("comparison.label.tolerancePercent")}: ${((parameter as RelativeToleranceParameter).tolerancePercent * 100).toFixed(0)}%`;
    }
    if (mode === ComparisonMode.TimeWindow && "windowHours" in parameter) {
      return `${t("comparison.label.windowHours")}: ${(parameter as TimeWindowParameter).windowHours}h`;
    }
    if (mode === ComparisonMode.RegexExtractNumber && "pattern" in parameter) {
      const pattern = (parameter as RegexExtractNumberParameter).pattern;
      return pattern ? `${t("comparison.label.regexPattern")}: ${pattern}` : null;
    }
    return null;
  };

  const parameterDisplay = getParameterDisplay();

  const tooltipContent = (
    <div className="flex flex-col gap-1 text-xs p-1">
      <div>
        <span className="text-default-400">{t("comparison.label.mode")}:</span>{" "}
        <span>{t(modeLabel)}</span>
      </div>
      {parameterDisplay && (
        <div>
          <span className="text-default-400">{parameterDisplay}</span>
        </div>
      )}
      {normalize && (
        <div>
          <span className="text-default-400">{t("comparison.label.normalize")}:</span>{" "}
          <span>{t("common.enabled")}</span>
        </div>
      )}
      <div>
        <span className="text-default-400">{t("comparison.label.weight")}:</span>{" "}
        <span>{weight}</span>
      </div>
      {isVeto && (
        <div>
          <span className="text-default-400">{t("comparison.label.vetoThreshold")}:</span>{" "}
          <span>{vetoThreshold}</span>
        </div>
      )}
      <div>
        <span className="text-default-400">{t("comparison.label.oneNullBehavior")}:</span>{" "}
        <span>{t(oneNullLabel)}</span>
      </div>
      <div>
        <span className="text-default-400">{t("comparison.label.bothNullBehavior")}:</span>{" "}
        <span>{t(bothNullLabel)}</span>
      </div>
    </div>
  );

  return (
    <Tooltip content={tooltipContent}>
      <Chip
        className={onClick ? "cursor-pointer" : undefined}
        size="sm"
        variant="flat"
        color={isVeto ? "warning" : "default"}
        onClick={onClick}
        onClose={onDelete}
      >
        <div className="flex items-center gap-1">
          {property && <PropertyTypeIcon type={property.type} />}
          <span>
            {property?.name || `${t("comparison.label.property")} #${propertyId}`}
          </span>
          <span className="text-default-400">|</span>
          <span className="text-default-500 text-xs">{t(modeLabel)}</span>
          {normalize && <span className="text-primary text-xs">[N]</span>}
          <span className="text-default-400">|</span>
          <span className="text-default-500 text-xs">×{weight}</span>
          {isVeto && <span className="text-danger text-xs">(Veto)</span>}
        </div>
      </Chip>
    </Tooltip>
  );
};

export default RuleDemonstrator;
