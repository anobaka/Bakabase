"use client";

import type { ResourceSelectorValue } from "@/components/ResourceSelectorModal";
import type { IProperty } from "@/components/Property/models";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceSelectorModal from "@/components/ResourceSelectorModal";
import { SearchOperation, ResourceTag, StandardValueType } from "@/sdk/constants";
import { deserializeStandardValue, serializeStandardValue } from "@/components/StandardValue";
import { getDbValueType, getBizValueType } from "@/components/Property/PropertySystem";

interface ParentResourceValueRendererProps {
  /** The valueProperty from getFilterValueProperty API - determines how to serialize/deserialize values */
  property: IProperty;
  /** Current dbValue (serialized) */
  dbValue?: string;
  /** Current bizValue (serialized) */
  bizValue?: string;
  /** Search operation - determines if single or multiple selection */
  operation?: SearchOperation;
  /** Called when value changes */
  onValueChange?: (dbValue?: string, bizValue?: string) => void;
  /** Whether to start in editing mode */
  defaultEditing?: boolean;
  /** Size variant */
  size?: "sm" | "md" | "lg";
  /** Style variant */
  variant?: "default" | "light";
  /** Whether the value is readonly */
  isReadonly?: boolean;
}

const ParentResourceValueRenderer: React.FC<ParentResourceValueRendererProps> = ({
  property,
  dbValue,
  bizValue,
  operation,
  onValueChange,
  defaultEditing,
  size = "sm",
  variant = "light",
  isReadonly,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [isModalOpen, setIsModalOpen] = useState(defaultEditing && !dbValue);

  // Use PropertySystem to get correct value types from the valueProperty
  const dbValueType = getDbValueType(property.type);
  const bizValueType = getBizValueType(property.type);

  // Determine if multiple selection based on dbValueType (list types indicate multiple)
  const isMultiple = dbValueType === StandardValueType.ListString;

  // Parse current values using proper deserialization
  const parseValues = (): ResourceSelectorValue[] => {
    if (!dbValue) return [];
    try {
      const deserializedDbValue = deserializeStandardValue(dbValue, dbValueType);
      const deserializedBizValue = deserializeStandardValue(bizValue ?? "", bizValueType);

      if (isMultiple) {
        const ids = (deserializedDbValue as string[] ?? []).map(id => parseInt(id, 10));
        const names = deserializedBizValue as string[] ?? [];
        return ids.map((id, index) => ({
          id,
          displayName: names[index] ?? `Resource ${id}`,
        }));
      } else {
        const id = parseInt(deserializedDbValue as string ?? "", 10);
        const name = deserializedBizValue as string;
        return isNaN(id) ? [] : [{
          id,
          displayName: name ?? `Resource ${id}`,
        }];
      }
    } catch {
      return [];
    }
  };

  const currentValues = parseValues();

  const handleOpenModal = () => {
    if (isReadonly) return;

    const portal = createPortal(ResourceSelectorModal, {
      multiple: isMultiple,
      defaultSelectedIds: currentValues.map(v => v.id),
      defaultCriteria: { tags: [ResourceTag.IsParent] },
      title: t<string>(isMultiple ? "Select Parent Resources" : "Select Parent Resource"),
      onConfirm: (selectedResources: ResourceSelectorValue[]) => {
        if (selectedResources.length === 0) {
          onValueChange?.(undefined, undefined);
        } else if (isMultiple) {
          // Serialize using correct value types
          const dbIds = selectedResources.map(r => r.id.toString());
          const bizNames = selectedResources.map(r => r.displayName);
          const serializedDbValue = serializeStandardValue(dbIds, dbValueType);
          const serializedBizValue = serializeStandardValue(bizNames, bizValueType);
          onValueChange?.(serializedDbValue, serializedBizValue);
        } else {
          // Serialize single value using correct value types
          const resource = selectedResources[0];
          const serializedDbValue = serializeStandardValue(resource.id.toString(), dbValueType);
          const serializedBizValue = serializeStandardValue(resource.displayName, bizValueType);
          onValueChange?.(serializedDbValue, serializedBizValue);
        }
        portal.destroy();
      },
      onCancel: () => {
        portal.destroy();
      },
    });
  };

  const handleRemoveValue = (valueToRemove: ResourceSelectorValue) => {
    const newValues = currentValues.filter(v => v.id !== valueToRemove.id);
    if (newValues.length === 0) {
      onValueChange?.(undefined, undefined);
    } else {
      const dbIds = newValues.map(v => v.id.toString());
      const bizNames = newValues.map(v => v.displayName);
      const serializedDbValue = serializeStandardValue(dbIds, dbValueType);
      const serializedBizValue = serializeStandardValue(bizNames, bizValueType);
      onValueChange?.(serializedDbValue, serializedBizValue);
    }
  };

  const renderValue = () => {
    if (currentValues.length === 0) {
      return (
        <Button
          className="min-w-fit"
          color="default"
          size={size}
          variant={variant}
          onPress={handleOpenModal}
          isDisabled={isReadonly}
        >
          {t<string>("Select...")}
        </Button>
      );
    }

    if (isMultiple) {
      return (
        <div className="flex flex-wrap gap-1 items-center">
          {currentValues.map((value) => (
            <Chip
              key={value.id}
              size="sm"
              variant="flat"
              onClose={isReadonly ? undefined : () => handleRemoveValue(value)}
            >
              {value.displayName}
            </Chip>
          ))}
          {!isReadonly && (
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={handleOpenModal}
            >
              +
            </Button>
          )}
        </div>
      );
    }

    // Single selection
    const value = currentValues[0];
    return (
      <Button
        className="min-w-fit"
        color="default"
        size={size}
        variant={variant}
        onPress={handleOpenModal}
        isDisabled={isReadonly}
      >
        {value.displayName}
      </Button>
    );
  };

  return renderValue();
};

export default ParentResourceValueRenderer;
