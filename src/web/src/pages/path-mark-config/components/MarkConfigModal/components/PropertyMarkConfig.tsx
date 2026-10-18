"use client";

import type { MarkConfig } from "../types";
import type { IProperty } from "@/components/Property/models";

import { useState, useEffect } from "react";
import { EditOutlined } from "@ant-design/icons";

import DynamicValueConfig from "./DynamicValueConfig";
import FixedPropertyValueEditor from "./FixedPropertyValueEditor";

import { Button, Chip, RadioGroup, Radio } from "@/components/bakaui";
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
  const [loadingProperty, setLoadingProperty] = useState(false);
  const [propertyLoadFailed, setPropertyLoadFailed] = useState(false);
  const [propertyLoadAttempt, setPropertyLoadAttempt] = useState(0);
  const property =
    selectedProperty?.pool === config.propertyPool && selectedProperty?.id === config.propertyId
      ? selectedProperty
      : null;

  useEffect(() => {
    const controller = new AbortController();
    let active = true;

    const loadProperty = async () => {
      setSelectedProperty(null);
      setPropertyLoadFailed(false);
      if (config.propertyId != null && config.propertyPool != null) {
        setLoadingProperty(true);
        try {
          const response = await BApi.property.getPropertiesByPool(config.propertyPool, undefined, {
            signal: controller.signal,
            showErrorToast: false,
          });

          if (response.code) throw new Error(response.message);
          const properties = response.data || [];
          const loadedProperty = properties.find((p) => p.id === config.propertyId);

          if (!active) return;
          setSelectedProperty((loadedProperty as IProperty) ?? null);
          setPropertyLoadFailed(!loadedProperty);
        } catch {
          if (active) setPropertyLoadFailed(true);
        } finally {
          if (active) setLoadingProperty(false);
        }
      } else {
        setLoadingProperty(false);
      }
    };

    void loadProperty();

    return () => {
      active = false;
      controller.abort();
    };
  }, [config.propertyId, config.propertyPool, propertyLoadAttempt]);

  const handleSelectProperty = () => {
    createPortal(PropertySelector, {
      v2: true,
      pool: PropertyPool.Custom | PropertyPool.Reserved,
      multiple: false,
      onSubmit: async (properties: IProperty[]) => {
        if (properties.length > 0) {
          const selected = properties[0];
          const changed =
            selected.pool !== config.propertyPool || selected.id !== config.propertyId;

          setSelectedProperty(selected);
          updateConfig({
            propertyPool: selected.pool,
            propertyId: selected.id,
            ...(changed ? { fixedValue: undefined } : {}),
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
          {property ? (
            <Chip
              color="primary"
              variant="flat"
              onClose={() => {
                setSelectedProperty(null);
                updateConfig({
                  propertyPool: undefined,
                  propertyId: undefined,
                  fixedValue: undefined,
                });
              }}
            >
              {property.name}
            </Chip>
          ) : (
            <span className="text-sm text-default-400">
              {t(
                loadingProperty
                  ? "common.state.loading"
                  : "pathMarkConfig.empty.noPropertySelected",
              )}
            </span>
          )}
          <Button
            size="sm"
            startContent={<EditOutlined />}
            variant="flat"
            onPress={handleSelectProperty}
          >
            {property ? t("common.action.change") : t("pathMarkConfig.action.selectProperty")}
          </Button>
        </div>
        {propertyLoadFailed && (
          <div className="flex items-center gap-2 text-sm text-danger" role="alert">
            {t("pathMarkConfig.error.loadProperty")}
            <Button
              size="sm"
              variant="light"
              onPress={() => setPropertyLoadAttempt((attempt) => attempt + 1)}
            >
              {t("common.action.retry")}
            </Button>
          </div>
        )}
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
      {(config.valueType ?? PropertyValueType.Fixed) === PropertyValueType.Fixed ? (
        <div className="flex flex-col gap-1">
          <span className="text-sm">{t("pathMarkConfig.label.fixedValue")}</span>
          {property ? (
            <FixedPropertyValueEditor
              key={`${property.pool}:${property.id}`}
              property={property}
              t={t}
              value={config.fixedValue}
              onChange={(value) => updateConfig({ fixedValue: value })}
            />
          ) : (
            <span className="text-sm text-default-400">
              {t(
                loadingProperty
                  ? "common.state.loading"
                  : "pathMarkConfig.hint.selectPropertyForFixedValue",
              )}
            </span>
          )}
        </div>
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
