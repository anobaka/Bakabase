"use client";

import type { MarkConfig } from "../types";
import type { IProperty } from "@/components/Property/models";
import type { PreviewResultsByPath, PathMarkPreviewResult } from "../hooks/usePreview";

import { useState, useEffect } from "react";
import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";
import { Button, Chip, Input, NumberInput, RadioGroup, Radio } from "@/components/bakaui";
import { PathMatchMode, PropertyValueType, PropertyPool, PathMarkType, PathMarkApplyScope } from "@/sdk/constants";
import { EditOutlined } from "@ant-design/icons";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
  priority: number;
  onPriorityChange: (priority: number) => void;
  preview: {
    loading: boolean;
    results: PathMarkPreviewResult[];
    resultsByPath?: PreviewResultsByPath[];
    isMultiplePaths?: boolean;
    error: string | null;
    applyScope?: PathMarkApplyScope;
  };
};

const PropertyMarkConfig = ({ config, updateConfig, t, priority, onPriorityChange, preview }: Props) => {
  const { createPortal } = useBakabaseContext();
  const [selectedProperty, setSelectedProperty] = useState<IProperty | null>(null);

  // Load selected property info on mount if we have propertyId
  useEffect(() => {
    const loadProperty = async () => {
      if (config.propertyId && config.propertyPool) {
        try {
          const properties = (await BApi.property.getPropertiesByPool(config.propertyPool)).data || [];
          const property = properties.find(p => p.id === config.propertyId);
          if (property) {
            setSelectedProperty(property as IProperty);
          }
        } catch (e) {
          console.error("Failed to load property", e);
        }
      }
    };
    loadProperty();
  }, []);

  const handleSelectProperty = () => {
    createPortal(PropertySelector, {
      v2: true,
      pool: PropertyPool.Custom | PropertyPool.Reserved,
      multiple: false,
      onSubmit: async (properties: IProperty[]) => {
        if (properties.length > 0) {
          const property = properties[0];
          setSelectedProperty(property);
          updateConfig({
            propertyPool: property.pool,
            propertyId: property.id,
          });
        }
      },
    });
  };

  return (
    <>
      {/* Explanatory text */}
      <div className="bg-primary-50 text-primary-700 rounded p-2 text-xs">
        {t("pathMark.property.explanation")}
      </div>

      <MatchModeSelector
        config={config}
        updateConfig={updateConfig}
        t={t}
      />

      {/* Preview Results - placed after apply scope (part of MatchModeSelector) */}
      <PreviewResults
        loading={preview.loading}
        results={preview.results}
        resultsByPath={preview.resultsByPath}
        isMultiplePaths={preview.isMultiplePaths}
        error={preview.error}
        markType={PathMarkType.Property}
        t={t}
        applyScope={preview.applyScope}
      />

      <div className="border-t border-default-200 pt-2">
        <span className="text-sm font-medium text-default-600">{t("pathMarkConfig.label.propertySettings")}</span>
        <div className="text-xs text-default-400 mt-1">{t("pathMark.property.settingsDescription")}</div>
      </div>

      {/* Property Selector */}
      <div className="flex flex-col gap-1">
        <span className="text-sm">{t("pathMarkConfig.label.targetProperty")}</span>
        <div className="flex items-center gap-2">
          {selectedProperty ? (
            <Chip
              color="primary"
              variant="flat"
              onClose={() => {
                setSelectedProperty(null);
                updateConfig({ propertyPool: undefined, propertyId: undefined });
              }}
            >
              {selectedProperty.name}
            </Chip>
          ) : (
            <span className="text-sm text-default-400">{t("pathMarkConfig.empty.noPropertySelected")}</span>
          )}
          <Button
            size="sm"
            variant="flat"
            startContent={<EditOutlined />}
            onPress={handleSelectProperty}
          >
            {selectedProperty ? t("common.action.change") : t("pathMarkConfig.action.selectProperty")}
          </Button>
        </div>
      </div>

      {/* Value Type - Radio Group */}
      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium">{t("pathMarkConfig.label.valueType")}</span>
        <RadioGroup
          value={String(config.valueType ?? PropertyValueType.Fixed)}
          onValueChange={(value) => updateConfig({ valueType: Number(value) })}
          size="sm"
          orientation="horizontal"
        >
          <Radio value={String(PropertyValueType.Fixed)}>
            {t("pathMarkConfig.label.fixed")}
          </Radio>
          <Radio value={String(PropertyValueType.Dynamic)}>
            {t("pathMarkConfig.label.dynamic")}
          </Radio>
        </RadioGroup>
      </div>

      {config.valueType === PropertyValueType.Fixed ? (
        <Input
          label={t("pathMarkConfig.label.fixedValue")}
          size="sm"
          value={config.fixedValue ?? ""}
          onValueChange={(v) => updateConfig({ fixedValue: v })}
        />
      ) : (
        <>
          {/* Value Match Mode - Radio Group */}
          <div className="flex flex-col gap-1">
            <span className="text-sm font-medium">{t("pathMarkConfig.label.valueExtractionMode")}</span>
            <RadioGroup
              value={String(config.valueMatchMode ?? PathMatchMode.Layer)}
              onValueChange={(value) => updateConfig({ valueMatchMode: Number(value) })}
              size="sm"
              orientation="horizontal"
            >
              <Radio value={String(PathMatchMode.Layer)}>
                {t("pathMarkConfig.label.layer")}
              </Radio>
              <Radio value={String(PathMatchMode.Regex)}>
                {t("pathMarkConfig.label.regex")}
              </Radio>
            </RadioGroup>
          </div>

          {config.valueMatchMode === PathMatchMode.Layer ? (
            <NumberInput
              label={t("pathMarkConfig.label.valueLayer")}
              description={t("pathMarkConfig.tip.zeroEqualsMatchedItem")}
              size="sm"
              value={config.valueLayer ?? 0}
              onValueChange={(v) => updateConfig({ valueLayer: v })}
            />
          ) : (
            <Input
              label={t("pathMarkConfig.label.valueRegex")}
              placeholder={t("pathMarkConfig.tip.regexExample")}
              size="sm"
              value={config.valueRegex ?? ""}
              onValueChange={(v) => updateConfig({ valueRegex: v })}
            />
          )}
        </>
      )}

      {/* Priority at the bottom */}
      <div className="border-t border-default-200 pt-2">
        <NumberInput
          label={t("common.label.priority")}
          description={t("pathMark.priority.description")}
          size="sm"
          value={priority}
          onValueChange={(v) => onPriorityChange(v ?? 10)}
        />
      </div>
    </>
  );
};

PropertyMarkConfig.displayName = "PropertyMarkConfig";

export default PropertyMarkConfig;
