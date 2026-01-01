"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AppstoreOutlined } from "@ant-design/icons";

import type { Resource as ResourceModel } from "@/core/models/Resource";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { IProperty } from "@/components/Property/models";

import {
  Button,
  Divider,
  Modal,
  Radio,
  RadioGroup,
  Spinner,
  toast,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import {
  PropertyPool,
  PropertyValueScope,
  ResourceAdditionalItem,
  ResourceProperty as ResourcePropertyEnum,
} from "@/sdk/constants";
import Resource from "@/components/Resource";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import {
  serializeStandardValue,
  convertFromApiValue,
} from "@/components/StandardValue/helpers";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

import type { AggregatedValueState, AggregatedPropertyValue, PropertyKey } from "./types";
import { makePropertyKey } from "./types";
import PropertyChangeConfirmDialog from "./PropertyChangeConfirmDialog";

interface Props extends DestroyableProps {
  resourceIds: number[];
  initialResources?: ResourceModel[];
  onSubmitted?: () => void;
}

const BulkPropertyEditor: React.FC<Props> = ({
  resourceIds,
  initialResources,
  onSubmitted,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(!initialResources);
  const [updatingProperty, setUpdatingProperty] = useState<PropertyKey | null>(null);
  const [editingProperty, setEditingProperty] = useState<PropertyKey | null>(null);
  const [gridColumns, setGridColumns] = useState(6);
  const [hasAnyChanges, setHasAnyChanges] = useState(false);

  const [resources, setResources] = useState<ResourceModel[]>([]);
  const [propertyMap, setPropertyMap] = useState<Map<PropertyKey, IProperty>>(new Map());
  const [aggregatedValues, setAggregatedValues] = useState<Map<PropertyKey, AggregatedPropertyValue>>(new Map());

  // Load all data on mount
  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      // Use initial resources if provided, otherwise fetch
      let loadedResources: ResourceModel[];

      if (initialResources && initialResources.length > 0) {
        // Use provided resources
        loadedResources = initialResources;

        // Load properties in parallel (no need to fetch resources)
        const [builtinPropsRsp, customPropsRsp] = await Promise.all([
          // @ts-ignore - PropertyPool flags combination
          BApi.property.getPropertiesByPool(PropertyPool.Reserved | PropertyPool.Internal),
          BApi.customProperty.getAllCustomProperties(),
        ]);

        // Store property responses for later use
        const builtinProps = (builtinPropsRsp.data || []) as IProperty[];
        const customProps = (customPropsRsp.data || []) as IProperty[];

        // Process loaded data (skip resource fetching part)
        processLoadedData(loadedResources, builtinProps, customProps);
      } else {
        // Load resources and properties in parallel
        const [resourcesRsp, builtinPropsRsp, customPropsRsp] = await Promise.all([
          BApi.resource.getResourcesByKeys({
            ids: resourceIds,
            additionalItems: ResourceAdditionalItem.All,
          }),
          // @ts-ignore - PropertyPool flags combination
          BApi.property.getPropertiesByPool(PropertyPool.Reserved | PropertyPool.Internal),
          BApi.customProperty.getAllCustomProperties(),
        ]);

        loadedResources = (resourcesRsp.data || []) as ResourceModel[];
        const builtinProps = (builtinPropsRsp.data || []) as IProperty[];
        const customProps = (customPropsRsp.data || []) as IProperty[];

        processLoadedData(loadedResources, builtinProps, customProps);
      }
    } catch (error) {
      console.error("Failed to load bulk editor data:", error);
      toast.error(t("Failed to load data"));
    } finally {
      setLoading(false);
    }
  }, [resourceIds, initialResources, t]);

  // Helper function to process loaded data
  const processLoadedData = (loadedResources: ResourceModel[], builtinProps: IProperty[], customProps: IProperty[]) => {
    try {
      // Convert API values
      loadedResources.forEach((r) => {
        if (r.properties) {
          Object.values(r.properties).forEach((poolProps) => {
            Object.values(poolProps).forEach((prop) => {
              if (prop.values) {
                for (const v of prop.values) {
                  v.bizValue = convertFromApiValue(v.bizValue, prop.bizValueType!);
                  v.aliasAppliedBizValue = convertFromApiValue(v.aliasAppliedBizValue, prop.bizValueType!);
                  v.value = convertFromApiValue(v.value, prop.dbValueType!);
                }
              }
            });
          });
        }
      });

      setResources(loadedResources);

      const propMap = new Map<PropertyKey, IProperty>();

      // Add Internal property: only MediaLibraryV2Multi
      const mediaLibMultiProp = builtinProps.find(
        (p) => p.pool === PropertyPool.Internal && p.id === ResourcePropertyEnum.MediaLibraryV2Multi
      );
      if (mediaLibMultiProp) {
        propMap.set(makePropertyKey(PropertyPool.Internal, mediaLibMultiProp.id), mediaLibMultiProp);
      }

      // Add all Reserved properties
      builtinProps
        .filter((p) => p.pool === PropertyPool.Reserved)
        .forEach((p) => {
          propMap.set(makePropertyKey(PropertyPool.Reserved, p.id), p);
        });

      // Add all Custom properties
      customProps.forEach((p) => {
        propMap.set(makePropertyKey(PropertyPool.Custom, p.id), p);
      });

      setPropertyMap(propMap);

      // Aggregate values for each property
      const aggValues = aggregatePropertyValues(loadedResources, propMap);
      setAggregatedValues(aggValues);
    } catch (error) {
      console.error("Failed to process data:", error);
      toast.error(t("Failed to process data"));
    }
  };

  // Helper function to aggregate property values
  const aggregatePropertyValues = (
    loadedResources: ResourceModel[],
    propMap: Map<PropertyKey, IProperty>
  ): Map<PropertyKey, AggregatedPropertyValue> => {
    const aggValues = new Map<PropertyKey, AggregatedPropertyValue>();

    propMap.forEach((property, key) => {
      const originalValues = new Map<number, { dbValue?: string; bizValue?: string }>();
      const valueStrings: string[] = [];

      loadedResources.forEach((resource) => {
        let dbValue: any = undefined;
        let bizValue: any = undefined;

        // Special handling for MediaLibraryV2Multi - get from resource.mediaLibraries
        if (property.pool === PropertyPool.Internal && property.id === ResourcePropertyEnum.MediaLibraryV2Multi) {
          if (resource.mediaLibraries && resource.mediaLibraries.length > 0) {
            // Use library IDs as dbValue, names as bizValue
            dbValue = resource.mediaLibraries.map(ml => String(ml.id));
            bizValue = resource.mediaLibraries.map(ml => ml.name);
          }
        } else {
          const resourceProp = resource.properties?.[property.pool]?.[property.id];

          // Get manual value (or first non-empty value)
          if (resourceProp?.values) {
            // Prefer manual scope
            const manualValue = resourceProp.values.find((v) => v.scope === PropertyValueScope.Manual);
            if (manualValue?.value !== undefined) {
              dbValue = manualValue.value;
              bizValue = manualValue.bizValue ?? manualValue.aliasAppliedBizValue;
            } else {
              // Find first non-empty value
              const firstValue = resourceProp.values.find((v) => v.value !== undefined);
              if (firstValue) {
                dbValue = firstValue.value;
                bizValue = firstValue.bizValue ?? firstValue.aliasAppliedBizValue;
              }
            }
          }
        }

        const serializedDb = serializeStandardValue(dbValue, property.dbValueType);
        const serializedBiz = serializeStandardValue(bizValue, property.bizValueType);

        originalValues.set(resource.id, {
          dbValue: serializedDb,
          bizValue: serializedBiz,
        });

        valueStrings.push(serializedDb ?? "");
      });

      // Determine state
      const nonEmptyValues = valueStrings.filter((v) => v !== "" && v !== undefined && v !== null);
      const emptyCount = valueStrings.length - nonEmptyValues.length;
      let state: AggregatedValueState;
      let sharedDbValue: string | undefined;
      let sharedBizValue: string | undefined;

      if (nonEmptyValues.length === 0) {
        state = "empty";
      } else if (emptyCount > 0) {
        // Some resources have values, some don't
        state = "partial";
      } else {
        const allSame = nonEmptyValues.every((v) => v === nonEmptyValues[0]);
        if (allSame) {
          state = "consistent";
          const firstResource = loadedResources[0];
          const orig = originalValues.get(firstResource.id);
          sharedDbValue = orig?.dbValue;
          sharedBizValue = orig?.bizValue;
        } else {
          state = "mixed";
        }
      }

      aggValues.set(key, {
        state,
        dbValue: sharedDbValue,
        bizValue: sharedBizValue,
        originalValues,
      });
    });

    return aggValues;
  };

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Handle property value change - immediately apply with confirmation for mixed state
  const handleValueChange = async (property: IProperty, dbValue?: string, bizValue?: string) => {
    const key = makePropertyKey(property.pool, property.id);
    const aggValue = aggregatedValues.get(key);

    if (!aggValue) return;

    // Calculate affected resources
    const affectedResources: Array<{
      resourceId: number;
      resourceName: string;
      oldBizValue?: string;
    }> = [];

    resources.forEach((resource) => {
      const orig = aggValue.originalValues.get(resource.id);
      if (orig?.dbValue !== dbValue) {
        affectedResources.push({
          resourceId: resource.id,
          resourceName: resource.displayName || resource.fileName,
          oldBizValue: orig?.bizValue,
        });
      }
    });

    if (affectedResources.length === 0) {
      return; // No actual changes
    }

    // For mixed state, show confirmation dialog
    if (aggValue.state === "mixed") {
      createPortal(PropertyChangeConfirmDialog, {
        property,
        affectedResources,
        newBizValue: bizValue,
        onConfirm: async () => {
          await applyPropertyChange(property, dbValue, bizValue, affectedResources.map(r => r.resourceId));
        },
      });
    } else {
      // For consistent or empty state, apply directly
      await applyPropertyChange(property, dbValue, bizValue, affectedResources.map(r => r.resourceId));
    }
  };

  // Apply property change via API
  const applyPropertyChange = async (
    property: IProperty,
    dbValue: string | undefined,
    bizValue: string | undefined,
    affectedResourceIds: number[]
  ) => {
    const key = makePropertyKey(property.pool, property.id);
    setUpdatingProperty(key);

    try {
      const rsp = await BApi.resource.bulkPutResourcePropertyValue({
        resourceIds: affectedResourceIds,
        propertyId: property.id,
        isCustomProperty: property.pool === PropertyPool.Custom,
        value: dbValue,
      });

      if (rsp.code) {
        return;
      }

      // Update local state to reflect the change
      setAggregatedValues((prev) => {
        const next = new Map(prev);
        const oldAgg = prev.get(key);
        if (oldAgg) {
          // Update originalValues for affected resources
          const newOriginalValues = new Map(oldAgg.originalValues);
          affectedResourceIds.forEach((id) => {
            newOriginalValues.set(id, { dbValue, bizValue });
          });

          // Recalculate state
          const valueStrings = Array.from(newOriginalValues.values()).map(v => v.dbValue ?? "");
          const nonEmptyValues = valueStrings.filter((v) => v !== "" && v !== undefined && v !== null);
          const emptyCount = valueStrings.length - nonEmptyValues.length;

          let newState: AggregatedValueState;
          if (nonEmptyValues.length === 0) {
            newState = "empty";
          } else if (emptyCount > 0) {
            newState = "partial";
          } else {
            const allSame = nonEmptyValues.every((v) => v === nonEmptyValues[0]);
            if (allSame) {
              newState = "consistent";
            } else {
              newState = "mixed";
            }
          }

          next.set(key, {
            state: newState,
            dbValue: newState === "consistent" ? dbValue : undefined,
            bizValue: newState === "consistent" ? bizValue : undefined,
            originalValues: newOriginalValues,
          });
        }
        return next;
      });

      setHasAnyChanges(true);
      toast.success(t("Property updated successfully"));
    } catch (error) {
      console.error("Failed to update property:", error);
      toast.danger(t("Failed to update property"));
    } finally {
      setUpdatingProperty(null);
    }
  };

  const handleClose = () => {
    if (hasAnyChanges) {
      onSubmitted?.();
    }
    setVisible(false);
  };

  // Group properties by pool for display
  const groupedProperties = useMemo(() => {
    const internal: IProperty[] = [];
    const reserved: IProperty[] = [];
    const custom: IProperty[] = [];

    propertyMap.forEach((prop) => {
      switch (prop.pool) {
        case PropertyPool.Internal:
          internal.push(prop);
          break;
        case PropertyPool.Reserved:
          reserved.push(prop);
          break;
        case PropertyPool.Custom:
          custom.push(prop);
          break;
      }
    });

    return { internal, reserved, custom };
  }, [propertyMap]);

  const renderPropertyRow = (property: IProperty) => {
    const key = makePropertyKey(property.pool, property.id);
    const aggValue = aggregatedValues.get(key);
    const isUpdating = updatingProperty === key;
    const isEditing = editingProperty === key;

    if (!aggValue) return null;

    const renderValue = () => {
      if (isUpdating) {
        return <Spinner size="sm" />;
      }

      switch (aggValue.state) {
        case "consistent":
        case "empty":
          // Consistent: show actual value; Empty: PropertyValueRenderer shows "Not set" by default
          return (
            <PropertyValueRenderer
              property={property}
              dbValue={aggValue.dbValue}
              bizValue={aggValue.bizValue}
              size="sm"
              onValueChange={(newDbValue, newBizValue) => {
                handleValueChange(property, newDbValue, newBizValue);
              }}
            />
          );
        case "partial":
        case "mixed":
          // Partial/Mixed: show status button, or PropertyValueRenderer when editing
          if (isEditing) {
            return (
              <PropertyValueRenderer
                property={property}
                dbValue={undefined}
                bizValue={undefined}
                size="sm"
                defaultEditing
                onValueChange={(newDbValue, newBizValue) => {
                  setEditingProperty(null);
                  handleValueChange(property, newDbValue, newBizValue);
                }}
              />
            );
          }
          return (
            <Button
              size="sm"
              variant="light"
              color={aggValue.state === "mixed" ? "warning" : "default"}
              onPress={() => setEditingProperty(key)}
            >
              {aggValue.state === "mixed" ? t("Multiple values") : t("Partially set")}
            </Button>
          );
      }
    };

    return (
      <div
        key={key}
        className={`grid gap-2 items-center py-1 px-2 rounded transition-colors hover:bg-default-100 ${
          isUpdating ? "opacity-50 pointer-events-none" : ""
        }`}
        style={{ gridTemplateColumns: "160px 1fr" }}
      >
        {/* Property name */}
        <div className="flex items-center gap-1">
          <BriefProperty fields={["pool", "name"]} property={property} />
        </div>

        {/* Value display/editor */}
        <div className="flex items-center gap-1 min-w-0">
          {renderValue()}
        </div>
      </div>
    );
  };

  const renderPropertySection = (_title: string, properties: IProperty[]) => {
    if (properties.length === 0) return null;

    return (
      <div className="flex flex-col">
        {properties.map((prop) => renderPropertyRow(prop))}
      </div>
    );
  };

  const columnOptions = [2, 4, 6, 8];

  return (
    <Modal
      size="5xl"
      title={t("Bulk edit properties")}
      visible={visible}
      footer={{
        actions: ["ok"],
        okProps: {
          children: t("Close"),
        },
      }}
      onClose={handleClose}
      onDestroyed={onDestroyed}
      onOk={handleClose}
      className="max-w-7xl"
    >
      {loading ? (
        <div className="flex items-center justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : (
        <div className="flex gap-4 h-[60vh]">
          {/* Left panel: Resource grid */}
          <div className="w-2/3 overflow-hidden flex flex-col">
            <div className="flex items-center justify-between mb-1">
              <div className="text-xs text-default-500">
                {t("{{count}} resources selected", { count: resources.length })}
              </div>
              <div className="flex items-center gap-1">
                <AppstoreOutlined className="text-default-400" />
                <RadioGroup
                  orientation="horizontal"
                  size="sm"
                  value={String(gridColumns)}
                  onValueChange={(v) => setGridColumns(parseInt(v, 10))}
                >
                  {columnOptions.map((col) => (
                    <Radio key={col} value={String(col)}>
                      {col}
                    </Radio>
                  ))}
                </RadioGroup>
              </div>
            </div>
            <div
              className="flex-1 overflow-auto border rounded p-1"
              style={{ borderColor: "var(--bakaui-overlap-background)" }}
            >
              <div
                className="grid gap-1"
                style={{ gridTemplateColumns: `repeat(${gridColumns}, 1fr)` }}
              >
                {resources.map((resource) => (
                  <Resource key={resource.id} resource={resource} />
                ))}
              </div>
            </div>
          </div>

          {/* Right panel: Property editor */}
          <div className="flex-1 flex flex-col min-w-0">
            <div className="text-sm font-medium mb-1">{t("Properties")}</div>
            <div
              className="flex-1 overflow-auto border rounded"
              style={{ borderColor: "var(--bakaui-overlap-background)" }}
            >
              <div className="flex flex-col gap-0.5 py-1">
                {renderPropertySection(t("Media Library"), groupedProperties.internal)}
                {groupedProperties.internal.length > 0 && groupedProperties.reserved.length > 0 && (
                  <Divider className="my-0.5" />
                )}
                {renderPropertySection(t("Reserved Properties"), groupedProperties.reserved)}
                {(groupedProperties.internal.length > 0 || groupedProperties.reserved.length > 0) &&
                  groupedProperties.custom.length > 0 && <Divider className="my-0.5" />}
                {renderPropertySection(t("Custom Properties"), groupedProperties.custom)}
                {propertyMap.size === 0 && (
                  <div className="text-center text-default-400 py-4">
                    {t("No properties available")}
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      )}
    </Modal>
  );
};

BulkPropertyEditor.displayName = "BulkPropertyEditor";

export default BulkPropertyEditor;
