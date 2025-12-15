"use client";

import type { MarkConfig } from "../types";
import type { IProperty } from "@/components/Property/models";

import { useState, useEffect } from "react";
import { usePreview } from "../hooks/usePreview";
import MatchModeSelector from "./MatchModeSelector";
import PreviewResults from "./PreviewResults";
import { Input, RadioGroup, Radio, Button, Chip } from "@/components/bakaui";
import { PathMarkType, PathMatchMode, PropertyValueType, PropertyPool } from "@/sdk/constants";
import { EditOutlined } from "@ant-design/icons";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  rootPath?: string;
  t: (key: string) => string;
};

const PropertyMarkConfig = ({ config, updateConfig, rootPath, t }: Props) => {
  const preview = usePreview(rootPath, PathMarkType.Property, config);
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
      <MatchModeSelector config={config} updateConfig={updateConfig} t={t} />

      <PreviewResults
        loading={preview.loading}
        results={preview.results}
        error={preview.error}
        t={t}
      />

      <div className="border-t border-default-200 pt-3">
        <span className="text-sm font-medium text-default-600">{t("Property Settings")}</span>
      </div>

      {/* Property Selector */}
      <div className="flex flex-col gap-2">
        <span className="text-sm">{t("Target Property")}</span>
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
            <span className="text-sm text-default-400">{t("No property selected")}</span>
          )}
          <Button
            size="sm"
            variant="flat"
            startContent={<EditOutlined />}
            onPress={handleSelectProperty}
          >
            {selectedProperty ? t("Change") : t("Select Property")}
          </Button>
        </div>
      </div>

      {/* Value Type - Radio Group */}
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">{t("Value Type")}</span>
        <RadioGroup
          value={String(config.valueType ?? PropertyValueType.Fixed)}
          onValueChange={(value) => updateConfig({ valueType: Number(value) })}
          size="sm"
          orientation="horizontal"
        >
          <Radio value={String(PropertyValueType.Fixed)} description={t("Use a fixed value for all matched items")}>
            {t("Fixed")}
          </Radio>
          <Radio value={String(PropertyValueType.Dynamic)} description={t("Extract value dynamically from file/folder name at a specific layer or using regex")}>
            {t("Dynamic")}
          </Radio>
        </RadioGroup>
      </div>

      {config.valueType === PropertyValueType.Fixed ? (
        <Input
          label={t("Fixed Value")}
          size="sm"
          value={config.fixedValue ?? ""}
          onValueChange={(v) => updateConfig({ fixedValue: v })}
        />
      ) : (
        <>
          {/* Value Match Mode - Radio Group */}
          <div className="flex flex-col gap-2">
            <span className="text-sm font-medium">{t("Value Extraction Mode")}</span>
            <RadioGroup
              value={String(config.valueMatchMode ?? PathMatchMode.Layer)}
              onValueChange={(value) => updateConfig({ valueMatchMode: Number(value) })}
              size="sm"
              orientation="horizontal"
            >
              <Radio value={String(PathMatchMode.Layer)} description={t("Use the folder/file name at a specific level as the value")}>
                {t("Layer")}
              </Radio>
              <Radio value={String(PathMatchMode.Regex)} description={t("Extract value using a regex capture group from the path")}>
                {t("Regex")}
              </Radio>
            </RadioGroup>
          </div>

          {config.valueMatchMode === PathMatchMode.Layer ? (
            <Input
              label={t("Value Layer")}
              description={t("The folder level from which to extract the value (0 = matched item)")}
              type="number"
              size="sm"
              value={String(config.valueLayer ?? 0)}
              onValueChange={(v) => updateConfig({ valueLayer: parseInt(v) || 0 })}
            />
          ) : (
            <Input
              label={t("Value Regex")}
              description={t("Regex with capture group to extract value, e.g., '\\[(.+?)\\]' to capture text in brackets")}
              size="sm"
              value={config.valueRegex ?? ""}
              onValueChange={(v) => updateConfig({ valueRegex: v })}
            />
          )}
        </>
      )}
    </>
  );
};

PropertyMarkConfig.displayName = "PropertyMarkConfig";

export default PropertyMarkConfig;
