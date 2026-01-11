"use client";

"use strict";

import type {
  BulkModificationProcessValue,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";
import type { PropertyPool, StandardValueType } from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useCallback, useEffect, useRef, useState } from "react";
import { ExclamationCircleOutlined } from "@ant-design/icons";

import {
  BulkModificationProcessorValueType,
  bulkModificationProcessorValueTypes,
  PropertyType,
} from "@/sdk/constants";
import {
  Button,
  Checkbox,
  Radio,
  RadioGroup,
  Select,
  Tooltip,
} from "@/components/bakaui";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { buildLogger } from "@/components/utils";
import BApi from "@/sdk/BApi";
import TypeMismatchTip from "@/pages/bulk-modification/components/BulkModification/ProcessValue/components/TypeMismatchTip";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { PropertyLabel } from "@/components/Property";

type Props = {
  onChange?: (value: BulkModificationProcessValue) => any;
  variables?: BulkModificationVariable[];
  value?: BulkModificationProcessValue;
  baseValueType: PropertyType;
  preferredProperty?: IProperty;
  availableValueTypes?: BulkModificationProcessorValueType[];
};

type PropertyTypeForManuallySettingValue = {
  type: PropertyType;
  isAvailable: boolean;
  unavailableReason?: string;
  properties?: IProperty[];
  bizValueType: StandardValueType;
  dbValueType: StandardValueType;
  isReferenceValueType: boolean;
};

const log = buildLogger("BulkModificationProcessValueEditor");

const Editor = (props: Props) => {
  const {
    onChange,
    variables,
    value: propsValue,
    baseValueType,
    preferredProperty,
    availableValueTypes,
  } = props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [propertyTypesForManuallySettingValue, setPropertyTypesForManuallySettingValue] = useState<
    PropertyTypeForManuallySettingValue[]
  >([]);
  const [value, setValue] = useState<BulkModificationProcessValue>(
    propsValue ?? {
      type: availableValueTypes?.[0] ?? BulkModificationProcessorValueType.ManuallyInput,
    },
  );
  const valueRef = useRef(value);

  useEffect(() => {
    valueRef.current = value;
  }, [value]);

  log(value, preferredProperty, propertyTypesForManuallySettingValue);

  useEffect(() => {
    BApi.property.getAvailablePropertyTypesForManuallySettingValue().then((r) => {
      const pvs = r.data || [];
      setPropertyTypesForManuallySettingValue(pvs);
    });
  }, []);

  const changeValue = useCallback(
    (patches: Partial<BulkModificationProcessValue>, triggerOnChange: boolean = true) => {
      const nv = {
        ...valueRef.current,
        ...patches,
      };

      switch (nv.type) {
        case BulkModificationProcessorValueType.ManuallyInput: {
          nv.editorPropertyType ??=
            preferredProperty?.type ??
            propertyTypesForManuallySettingValue?.[0]?.type ??
            baseValueType;

          if (!nv.property) {
            const pv = propertyTypesForManuallySettingValue.find(
              (pt) => pt.type == nv.editorPropertyType,
            );
            const property =
              (pv?.properties?.find((p) => p.id == nv.propertyId && p.pool == nv.propertyPool) ??
              preferredProperty?.type == nv.editorPropertyType)
                ? preferredProperty
                : pv?.properties?.[0];

            if (property) {
              nv.property = property;
              nv.propertyId = property.id;
              nv.propertyPool = property.pool;
            }
          }
          break;
        }
        case BulkModificationProcessorValueType.Variable: {
          break;
        }
      }

      setValue(nv);

      if (triggerOnChange) {
        onChange?.(nv);
      }
    },
    [preferredProperty, propertyTypesForManuallySettingValue, baseValueType, onChange],
  );

  useEffect(() => {
    changeValue(valueRef.current, false);
  }, [changeValue, propertyTypesForManuallySettingValue]);

  const onFollowPropertyChanges = async (follow: boolean) => {
    const { property } = value;

    if (!property) {
      return;
    }

    if (value.value) {
      if (!value.followPropertyChanges && follow) {
        value.value = deserializeStandardValue(
          (
            await BApi.property.getPropertyDbValue(property.pool, property.id, {
              bizValue: value.value,
            })
          )?.data ?? null,
          property.dbValueType,
        );
      } else {
        if (value.followPropertyChanges && !follow) {
          value.value = deserializeStandardValue(
            (
              await BApi.property.getPropertyBizValue(property.pool, property.id, {
                dbValue: value.value,
              })
            )?.data ?? null,
            property.bizValueType,
          );
        }
      }
    }

    value.followPropertyChanges = follow;
    changeValue({ ...value });
  };

  const valueTypes: BulkModificationProcessorValueType[] =
    availableValueTypes ?? bulkModificationProcessorValueTypes.map((x) => x.value);

  // Get current property type info
  const currentPropertyTypeInfo = propertyTypesForManuallySettingValue.find(
    (pt) => pt.type == value.editorPropertyType,
  );
  const isReferenceType = currentPropertyTypeInfo?.isReferenceValueType ?? false;

  // Open PropertySelector modal for reference types
  const openPropertySelector = () => {
    if (!currentPropertyTypeInfo?.properties) return;

    const validPropertyIds = new Set(
      currentPropertyTypeInfo.properties.map((p) => `${p.pool}-${p.id}`),
    );

    createPortal(PropertySelector, {
      v2: true,
      pool: (1 | 2 | 4) as PropertyPool, // All pools
      multiple: false,
      types: value.editorPropertyType ? [value.editorPropertyType] : undefined,
      selection:
        value.propertyId && value.propertyPool
          ? [{ id: value.propertyId, pool: value.propertyPool }]
          : undefined,
      isDisabled: (p) => !validPropertyIds.has(`${p.pool}-${p.id}`),
      onSubmit: async (selectedProperties) => {
        const prop = selectedProperties[0];
        if (prop) {
          changeValue({
            propertyPool: prop.pool,
            propertyId: prop.id,
            property: prop,
            value: undefined,
            followPropertyChanges: currentPropertyTypeInfo?.isReferenceValueType,
          });
        }
      },
    });
  };

  // Get type mismatch info
  const getSelectedType = (): PropertyType | undefined => {
    switch (value.type) {
      case BulkModificationProcessorValueType.ManuallyInput:
        return value.editorPropertyType;
      case BulkModificationProcessorValueType.Variable:
        return variables?.find((v) => v.key == value.value)?.property.type;
    }
  };

  const selectedType = getSelectedType();

  return (
    <div className="flex flex-col gap-3">
      {/* Row 1: Value Source Selection */}
      {valueTypes.length > 1 && (
        <RadioGroup
          label={t<string>("bulkModification.valueSource.label")}
          orientation="horizontal"
          value={String(value.type)}
          onValueChange={(v) => {
            const type = parseInt(v, 10) as BulkModificationProcessorValueType;
            changeValue({ type });
          }}
        >
          {valueTypes.includes(BulkModificationProcessorValueType.ManuallyInput) && (
            <Radio value={String(BulkModificationProcessorValueType.ManuallyInput)}>
              {t<string>("bulkModification.valueType.manuallyInput")}
            </Radio>
          )}
          {valueTypes.includes(BulkModificationProcessorValueType.Variable) && (
            <Radio value={String(BulkModificationProcessorValueType.Variable)}>
              {t<string>("bulkModification.valueType.variable")}
            </Radio>
          )}
        </RadioGroup>
      )}

      {/* Row 2: Type/Variable Selection */}
      {value.type === BulkModificationProcessorValueType.ManuallyInput ? (
        <div className="flex items-center gap-2 flex-wrap">
          <Select
            className="max-w-xs"
            label={t<string>("bulkModification.valueType.label")}
            dataSource={propertyTypesForManuallySettingValue.map((pt) => ({
              label: (
                <div className="flex items-center gap-2">
                  {t<string>(`PropertyType.${PropertyType[pt.type]}`)}
                  {!pt.isAvailable && (
                    <Tooltip
                      className="max-w-[400px]"
                      color="warning"
                      content={pt.unavailableReason}
                    >
                      <ExclamationCircleOutlined className="text-base" />
                    </Tooltip>
                  )}
                </div>
              ),
              textValue: t<string>(`PropertyType.${PropertyType[pt.type]}`),
              value: String(pt.type),
              isDisabled: !pt.isAvailable,
            }))}
            selectedKeys={value.editorPropertyType ? [value.editorPropertyType.toString()] : []}
            selectionMode="single"
            onSelectionChange={(keys) => {
              const editorPropertyType = parseInt(
                Array.from(keys)[0] as string,
                10,
              ) as PropertyType;

              changeValue({
                editorPropertyType,
                property: undefined,
                propertyId: undefined,
                propertyPool: undefined,
                value: undefined,
                followPropertyChanges: undefined,
              });
            }}
          />
          {selectedType && <TypeMismatchTip fromType={selectedType} toType={baseValueType} />}
        </div>
      ) : (
        (!variables || variables.length === 0) ? (
          <span className="text-default-400">
            {t<string>("bulkModification.label.noVariablesAvailable")}
          </span>
        ) : (
          <div className="flex items-center gap-2 flex-wrap">
            <Select
              className="max-w-xs"
              isRequired
              label={t<string>("bulkModification.select.variable")}
              dataSource={variables.map((v) => ({
                label: v.name,
                value: v.key,
              }))}
              placeholder={t<string>("bulkModification.select.variable")}
              selectedKeys={value.value ? [value.value] : []}
              selectionMode="single"
              onSelectionChange={(keys) => {
                const key = Array.from(keys)[0] as string;
                changeValue({
                  value: key,
                  editorPropertyType: undefined,
                  property: undefined,
                  propertyId: undefined,
                  propertyPool: undefined,
                  followPropertyChanges: undefined,
                });
              }}
            />
            {selectedType && <TypeMismatchTip fromType={selectedType} toType={baseValueType} />}
          </div>
        )
      )}

      {/* Row 3: Property Selection (for reference types only) */}
      {value.type === BulkModificationProcessorValueType.ManuallyInput &&
        isReferenceType &&
        currentPropertyTypeInfo?.isAvailable && (
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-sm text-default-500">
              {t<string>("bulkModification.dataSource.label")}
            </span>
            <Button variant="bordered" onPress={openPropertySelector}>
              {value.property
                ? <PropertyLabel showPool property={value.property} />
                : t<string>("bulkModification.dataSource.clickToSelect")}
            </Button>
            {value.property && (
              <Tooltip
                color="secondary"
                content={
                  <div className="flex flex-col gap-1 max-w-[400px]">
                    <p>{t<string>("bulkModification.label.followPropertyChangesDescription1")}</p>
                    <p>{t<string>("bulkModification.label.followPropertyChangesDescription2")}</p>
                    <p>{t<string>("bulkModification.label.followPropertyChangesDescription3")}</p>
                  </div>
                }
              >
                <Checkbox
                  defaultSelected
                  isSelected={value.followPropertyChanges}
                  onValueChange={onFollowPropertyChanges}
                >
                  {t<string>("bulkModification.label.followPropertyChanges")}
                </Checkbox>
              </Tooltip>
            )}
          </div>
        )}

      {/* Row 4: Value Input */}
      {value.type === BulkModificationProcessorValueType.ManuallyInput &&
        value.property &&
        currentPropertyTypeInfo?.isAvailable && (
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-sm text-default-500">
              {t<string>("bulkModification.label.setValue")}
            </span>
            <PropertyValueRenderer
              bizValue={value.followPropertyChanges ? undefined : value.value}
              dbValue={value.followPropertyChanges ? value.value : undefined}
              isEditing
              property={value.property}
              variant="light"
              onValueChange={(dv, bv) => {
                changeValue({
                  value: value.followPropertyChanges ? dv : bv,
                });
              }}
              size="sm"
            />
          </div>
        )}
    </div>
  );
};

Editor.displayName = "Editor";

export default Editor;
