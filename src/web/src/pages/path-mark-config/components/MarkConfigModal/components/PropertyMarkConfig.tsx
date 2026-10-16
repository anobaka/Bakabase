"use client";

import type { MarkConfig } from "../types";
import type { IProperty } from "@/components/Property/models";

import { useState, useEffect } from "react";
import { EditOutlined } from "@ant-design/icons";

import DynamicValueConfig from "./DynamicValueConfig";

import { Button, Chip, Input, RadioGroup, Radio } from "@/components/bakaui";
import { PropertyValueType, PropertyPool, PathMarkType } from "@/sdk/constants";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
};

const PropertyMarkConfig = ({ config, updateConfig, t }: Props) => {
  const { createPortal } = useBakabaseContext();
  const [selectedProperty, setSelectedProperty] = useState<IProperty | null>(null);

  // Load selected property info on mount if we have propertyId
  useEffect(() => {
    const loadProperty = async () => {
      if (config.propertyId && config.propertyPool) {
        try {
          const properties =
            (await BApi.property.getPropertiesByPool(config.propertyPool)).data || [];
          const property = properties.find((p) => p.id === config.propertyId);

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
    <div className="flex flex-col gap-4">
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
            <span className="text-sm text-default-400">
              {t("pathMarkConfig.empty.noPropertySelected")}
            </span>
          )}
          <Button
            size="sm"
            startContent={<EditOutlined />}
            variant="flat"
            onPress={handleSelectProperty}
          >
            {selectedProperty
              ? t("common.action.change")
              : t("pathMarkConfig.action.selectProperty")}
          </Button>
        </div>
      </div>

      <RadioGroup
        label={t("pathMarkConfig.label.valueType")}
        orientation="horizontal"
        size="sm"
        value={String(config.valueType ?? PropertyValueType.Fixed)}
        onValueChange={(value) => updateConfig({ valueType: Number(value) })}
      >
        <Radio value={String(PropertyValueType.Fixed)}>{t("pathMarkConfig.label.fixed")}</Radio>
        <Radio value={String(PropertyValueType.Dynamic)}>{t("pathMarkConfig.label.dynamic")}</Radio>
      </RadioGroup>
      {config.valueType === PropertyValueType.Fixed ? (
        <Input
          label={t("pathMarkConfig.label.fixedValue")}
          size="sm"
          value={config.fixedValue ?? ""}
          onValueChange={(value) => updateConfig({ fixedValue: value })}
        />
      ) : (
        <DynamicValueConfig
          config={config}
          markType={PathMarkType.Property}
          t={t}
          updateConfig={updateConfig}
        />
      )}
    </div>
  );
};

export default PropertyMarkConfig;
