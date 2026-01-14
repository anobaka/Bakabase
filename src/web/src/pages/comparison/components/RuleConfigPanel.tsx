"use client";

import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useEffect } from "react";
import { useNavigate } from "react-router-dom";

import {
  Button,
  Input,
  Switch,
  Chip,
  Select,
  Modal,
  Link,
} from "@/components/bakaui";
import { PropertyPool, PropertyType, ComparisonMode, NullValueBehavior } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertySelector from "@/components/PropertySelector";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import ScoringExplanation from "./ScoringExplanation";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

export const AllComparisonModes = [
  { value: ComparisonMode.StrictEqual, label: "comparison.mode.strictEqual", description: "comparison.mode.strictEqual.description" },
  { value: ComparisonMode.TextSimilarity, label: "comparison.mode.textSimilarity", description: "comparison.mode.textSimilarity.description" },
  { value: ComparisonMode.RegexExtractNumber, label: "comparison.mode.regexExtractNumber", description: "comparison.mode.regexExtractNumber.description" },
  { value: ComparisonMode.FixedTolerance, label: "comparison.mode.fixedTolerance", description: "comparison.mode.fixedTolerance.description" },
  { value: ComparisonMode.RelativeTolerance, label: "comparison.mode.relativeTolerance", description: "comparison.mode.relativeTolerance.description" },
  { value: ComparisonMode.SetIntersection, label: "comparison.mode.setIntersection", description: "comparison.mode.setIntersection.description" },
  { value: ComparisonMode.Subset, label: "comparison.mode.subset", description: "comparison.mode.subset.description" },
  { value: ComparisonMode.TimeWindow, label: "comparison.mode.timeWindow", description: "comparison.mode.timeWindow.description" },
  { value: ComparisonMode.SameDay, label: "comparison.mode.sameDay", description: "comparison.mode.sameDay.description" },
  { value: ComparisonMode.ExtensionMap, label: "comparison.mode.extensionMap", description: "comparison.mode.extensionMap.description" },
];

export const getAvailableModesForPropertyType = (propertyType: PropertyType): ComparisonMode[] => {
  switch (propertyType) {
    case PropertyType.SingleLineText:
    case PropertyType.MultilineText:
    case PropertyType.Link:
      return [
        ComparisonMode.StrictEqual,
        ComparisonMode.TextSimilarity,
        ComparisonMode.RegexExtractNumber,
      ];
    case PropertyType.Number:
    case PropertyType.Percentage:
    case PropertyType.Rating:
      return [
        ComparisonMode.StrictEqual,
        ComparisonMode.FixedTolerance,
        ComparisonMode.RelativeTolerance,
      ];
    case PropertyType.Boolean:
      return [ComparisonMode.StrictEqual];
    case PropertyType.SingleChoice:
      return [
        ComparisonMode.StrictEqual,
        ComparisonMode.TextSimilarity,
        ComparisonMode.RegexExtractNumber,
      ];
    case PropertyType.MultipleChoice:
    case PropertyType.Tags:
    case PropertyType.Multilevel:
      return [
        ComparisonMode.StrictEqual,
        ComparisonMode.SetIntersection,
        ComparisonMode.Subset,
      ];
    case PropertyType.Date:
    case PropertyType.DateTime:
    case PropertyType.Time:
      return [
        ComparisonMode.StrictEqual,
        ComparisonMode.TimeWindow,
        ComparisonMode.SameDay,
      ];
    case PropertyType.Attachment:
      return [
        ComparisonMode.StrictEqual,
        ComparisonMode.ExtensionMap,
      ];
    case PropertyType.Formula:
      return [ComparisonMode.StrictEqual];
    default:
      return [ComparisonMode.StrictEqual];
  }
};

export const NullValueBehaviors = [
  { value: NullValueBehavior.Skip, label: "comparison.nullBehavior.skip", description: "comparison.nullBehavior.skip.description" },
  { value: NullValueBehavior.Fail, label: "comparison.nullBehavior.fail", description: "comparison.nullBehavior.fail.description" },
  { value: NullValueBehavior.Pass, label: "comparison.nullBehavior.pass", description: "comparison.nullBehavior.pass.description" },
];

// Mode-specific parameter types
export interface FixedToleranceParameter {
  tolerance: number;
}

export interface RelativeToleranceParameter {
  tolerancePercent: number;
}

export interface TimeWindowParameter {
  windowHours: number;
}

export interface RegexExtractNumberParameter {
  pattern: string;
}

export type ModeParameter =
  | FixedToleranceParameter
  | RelativeToleranceParameter
  | TimeWindowParameter
  | RegexExtractNumberParameter
  | null;

export const getDefaultParameterForMode = (mode: ComparisonMode): ModeParameter => {
  switch (mode) {
    case ComparisonMode.FixedTolerance:
      return { tolerance: 1 };
    case ComparisonMode.RelativeTolerance:
      return { tolerancePercent: 0.1 };
    case ComparisonMode.TimeWindow:
      return { windowHours: 24 };
    case ComparisonMode.RegexExtractNumber:
      return { pattern: "" };
    default:
      return null;
  }
};

export interface RuleConfig {
  propertyPool?: number;
  propertyId?: number;
  property?: IProperty;
  mode: ComparisonMode;
  parameter?: ModeParameter;
  normalize: boolean;
  weight: number;
  isVeto: boolean;
  vetoThreshold: number;
  oneNullBehavior: NullValueBehavior;
  bothNullBehavior: NullValueBehavior;
}

export interface RuleConfigValidationError {
  field: string;
  message: string;
}

export const validateRuleConfig = (config: RuleConfig, t: (key: string) => string): RuleConfigValidationError[] => {
  const errors: RuleConfigValidationError[] = [];

  // Property is required
  if (!config.property) {
    errors.push({ field: "property", message: t("comparison.validation.propertyRequired") });
  }

  // RegexExtractNumber requires a non-empty pattern
  if (config.mode === ComparisonMode.RegexExtractNumber) {
    const pattern = (config.parameter as RegexExtractNumberParameter)?.pattern;
    if (!pattern || pattern.trim() === "") {
      errors.push({ field: "parameter.pattern", message: t("comparison.validation.regexPatternRequired") });
    }
  }

  // Weight must be positive
  if (config.weight <= 0) {
    errors.push({ field: "weight", message: t("comparison.validation.weightPositive") });
  }

  // Veto threshold must be between 0 and 1 when isVeto is true
  if (config.isVeto && (config.vetoThreshold < 0 || config.vetoThreshold > 1)) {
    errors.push({ field: "vetoThreshold", message: t("comparison.validation.vetoThresholdRange") });
  }

  return errors;
};

export interface RuleConfigPanelProps {
  value: RuleConfig;
  onChange: (value: RuleConfig) => void;
  /** If true, shows property selector. If false, assumes property is already set */
  showPropertySelector?: boolean;
  /** Validation errors to display */
  validationErrors?: RuleConfigValidationError[];
}

const RuleConfigPanel = ({ value, onChange, showPropertySelector = true, validationErrors = [] }: RuleConfigPanelProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const getError = (field: string) => validationErrors.find((e) => e.field === field);
  const hasError = (field: string) => !!getError(field);
  const navigate = useNavigate();

  const handleNavigateToSpecialText = () => {
    createPortal(Modal, {
      title: t("Confirm"),
      children: t("comparison.confirm.navigateToSpecialText"),
      footer: {
        actions: ["ok", "cancel"],
      },
      defaultVisible: true,
      onOk: () => {
        navigate("/text");
      },
    });
  };

  const availableModes = value.property
    ? getAvailableModesForPropertyType(value.property.type)
    : [ComparisonMode.StrictEqual];

  const filteredModes = AllComparisonModes.filter((m) => availableModes.includes(m.value));

  // Auto-switch mode if current mode is not available for property type
  useEffect(() => {
    if (value.property && !availableModes.includes(value.mode)) {
      onChange({ ...value, mode: availableModes[0] });
    }
  }, [value.property?.type]);

  const handleSelectProperty = () => {
    createPortal(PropertySelector, {
      pool: PropertyPool.All,
      multiple: false,
      onSubmit: async (selectedProperties: IProperty[]) => {
        if (selectedProperties.length > 0) {
          const property = selectedProperties[0];
          const newAvailableModes = getAvailableModesForPropertyType(property.type);
          onChange({
            ...value,
            propertyPool: property.pool,
            propertyId: property.id,
            property,
            mode: newAvailableModes.includes(value.mode) ? value.mode : newAvailableModes[0],
          });
        }
      },
    });
  };

  return (
    <div className="flex flex-col gap-4">
      {/* Property Selection */}
      {showPropertySelector && (
        <div className="flex flex-col gap-1">
          <label className="text-sm text-default-500">{t("comparison.label.property")}</label>
          <div className="flex items-center gap-2">
              <Button
                color="primary"
                size="sm"
                variant="flat"
                onPress={handleSelectProperty}
              >
                {value.property ? (
                  <BriefProperty property={value.property} fields={["pool", "type", "name"]} showPoolChip={false}/>
                ) : (
                  <span className="text-default-400">{t("comparison.action.selectProperty")}</span>
                )}
              </Button>
          </div>
        </div>
      )}

      {/* Mode - Only show if property is selected */}
      {value.property && (
        <div className="flex flex-col gap-1">
          <Select
            dataSource={filteredModes.map((mode) => ({
              value: String(mode.value),
              label: t(mode.label),
              description: t(mode.description),
            }))}
            label={t("comparison.label.mode")}
            selectedKeys={[String(value.mode)]}
            size="sm"
            isRequired
            onSelectionChange={(keys) => {
              const key = Array.from(keys)[0];
              const newMode = Number(key) as ComparisonMode;
              onChange({
                ...value,
                mode: newMode,
                parameter: getDefaultParameterForMode(newMode),
              });
            }}
          />
        </div>
      )}

      {/* Mode-specific parameters */}
      {value.property && value.mode === ComparisonMode.FixedTolerance && (
        <div className="flex flex-col gap-1">
          <Input
            className="w-48"
            label={t("comparison.label.tolerance")}
            description={t("comparison.description.tolerance")}
            size="sm"
            type="number"
            min={0}
            step={0.1}
            value={String((value.parameter as FixedToleranceParameter)?.tolerance ?? 1)}
            onValueChange={(v) => onChange({
              ...value,
              parameter: { tolerance: Number(v) },
            })}
          />
        </div>
      )}

      {value.property && value.mode === ComparisonMode.RelativeTolerance && (
        <div className="flex flex-col gap-1">
          <Input
            className="w-48"
            label={t("comparison.label.tolerancePercent")}
            description={t("comparison.description.tolerancePercent")}
            size="sm"
            type="number"
            min={0}
            max={1}
            step={0.05}
            value={String((value.parameter as RelativeToleranceParameter)?.tolerancePercent ?? 0.1)}
            onValueChange={(v) => onChange({
              ...value,
              parameter: { tolerancePercent: Number(v) },
            })}
          />
        </div>
      )}

      {value.property && value.mode === ComparisonMode.TimeWindow && (
        <div className="flex flex-col gap-1">
          <Input
            className="w-48"
            label={t("comparison.label.windowHours")}
            description={t("comparison.description.windowHours")}
            size="sm"
            type="number"
            min={0}
            step={1}
            value={String((value.parameter as TimeWindowParameter)?.windowHours ?? 24)}
            onValueChange={(v) => onChange({
              ...value,
              parameter: { windowHours: Number(v) },
            })}
          />
        </div>
      )}

      {value.property && value.mode === ComparisonMode.RegexExtractNumber && (
        <div className="flex flex-col gap-1">
          <Input
            className="w-full"
            label={t("comparison.label.regexPattern")}
            description={t("comparison.description.regexPattern")}
            size="sm"
            placeholder="(\d+)"
            isRequired
            isInvalid={hasError("parameter.pattern")}
            errorMessage={getError("parameter.pattern")?.message}
            value={(value.parameter as RegexExtractNumberParameter)?.pattern ?? ""}
            onValueChange={(v) => onChange({
              ...value,
              parameter: { pattern: v },
            })}
          />
        </div>
      )}

      {/* Normalize - Only show for text-based property types */}
      {value.property && [PropertyType.SingleLineText, PropertyType.MultilineText, PropertyType.Link, PropertyType.SingleChoice, PropertyType.MultipleChoice, PropertyType.Tags, PropertyType.Multilevel].includes(value.property.type) && (
        <div className="flex flex-col gap-1">
          <Switch
            isSelected={value.normalize}
            size="sm"
            onValueChange={(v) => onChange({ ...value, normalize: v })}
          >
            {t("comparison.label.normalize")}
          </Switch>
          <span className="text-xs text-default-400">
            {t("comparison.description.normalize")}
            {" "}
            <Link
              className="text-xs cursor-pointer"
              underline="always"
              onPress={handleNavigateToSpecialText}
            >
              {t("comparison.action.configureSpecialText")}
            </Link>
          </span>
        </div>
      )}

      {/* Weight */}
      {value.property && (
        <div className="flex flex-col gap-1">
          <Input
            className="w-32"
            label={t("comparison.label.weight")}
            size="sm"
            type="number"
            isRequired
            isInvalid={hasError("weight")}
            errorMessage={getError("weight")?.message}
            value={String(value.weight)}
            onValueChange={(v) => onChange({ ...value, weight: Number(v) })}
          />
          <ScoringExplanation mode="inline" className="mt-2 p-3 bg-default-100 rounded-lg" />
        </div>
      )}

      {/* Veto settings */}
      {value.property && (
        <div className="flex flex-col gap-2">
          <div className="flex flex-col gap-1">
            <Switch
              isSelected={value.isVeto}
              size="sm"
              onValueChange={(v) => onChange({ ...value, isVeto: v })}
            >
              {t("comparison.label.isVeto")}
            </Switch>
            <span className="text-xs text-default-400">{t("comparison.description.isVeto")}</span>
          </div>
          {value.isVeto && (
            <Input
              className="w-32"
              description={t("comparison.description.vetoThreshold")}
              label={t("comparison.label.vetoThreshold")}
              max={1}
              min={0}
              size="sm"
              step={0.1}
              type="number"
              isInvalid={hasError("vetoThreshold")}
              errorMessage={getError("vetoThreshold")?.message}
              value={String(value.vetoThreshold)}
              onValueChange={(v) => onChange({ ...value, vetoThreshold: Number(v) })}
            />
          )}
        </div>
      )}

      {/* Null value behaviors */}
      {value.property && (
        <div className="grid grid-cols-2 gap-4">
          <Select
            dataSource={NullValueBehaviors.map((b) => ({
              value: String(b.value),
              label: t(b.label),
              description: t(b.description),
            }))}
            description={t("comparison.description.oneNullBehavior")}
            label={t("comparison.label.oneNullBehavior")}
            selectedKeys={[String(value.oneNullBehavior)]}
            size="sm"
            onSelectionChange={(keys) => {
              const key = Array.from(keys)[0];
              onChange({ ...value, oneNullBehavior: Number(key) as NullValueBehavior });
            }}
          />
          <Select
            dataSource={NullValueBehaviors.map((b) => ({
              value: String(b.value),
              label: t(b.label),
              description: t(b.description),
            }))}
            description={t("comparison.description.bothNullBehavior")}
            label={t("comparison.label.bothNullBehavior")}
            selectedKeys={[String(value.bothNullBehavior)]}
            size="sm"
            onSelectionChange={(keys) => {
              const key = Array.from(keys)[0];
              onChange({ ...value, bothNullBehavior: Number(key) as NullValueBehavior });
            }}
          />
        </div>
      )}

      {/* Validation Errors - only show errors for fields without built-in error display */}
      {hasError("property") && (
        <div className="text-danger text-sm">
          {getError("property")?.message}
        </div>
      )}
    </div>
  );
};

export default RuleConfigPanel;
