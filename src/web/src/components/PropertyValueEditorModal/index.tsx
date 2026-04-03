"use client";

/**
 * PropertyValueEditorModal - Generic modal for selecting/entering a property value.
 *
 * For reference types with no options:
 *   Shows a "Modify property configuration" button that opens PropertyModal,
 *   then reloads the property data so users can select from the newly added options.
 *
 * For all types:
 *   Uses PropertyValueRenderer(Editor) for value input.
 *   Returns bizValue via onSubmit callback.
 */

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { SettingOutlined } from "@ant-design/icons";

import { Button, Modal } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";

const log = buildLogger("PropertyValueEditorModal");
import { PropertyPool, PropertyType } from "@/sdk/constants";
import { isReferenceValueType } from "@/components/Property/PropertySystem";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyModal from "@/components/PropertyModal";
import BApi from "@/sdk/BApi";

interface Props extends DestroyableProps {
  property: IProperty;
  /** Title override. Defaults to property name. */
  title?: string;
  onSubmit: (dbValue: string) => void;
}

const PropertyValueEditorModal: React.FC<Props> = ({
  property: initialProperty,
  title,
  onSubmit,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const isReference = isReferenceValueType(initialProperty.type);

  const [property, setProperty] = useState<IProperty>(initialProperty);
  // Track the latest dbValue from the editor. Use empty string as initial
  // to distinguish "not yet changed" from "changed to undefined/null".
  const [selectedDbValue, setSelectedDbValue] = useState<string | null>(null);
  const [editorKey, setEditorKey] = useState(0);

  // Check if reference property has options
  const hasOptions = (() => {
    if (!isReference) return true;
    const opts = property.options;
    if (!opts) return false;
    if (property.type === PropertyType.SingleChoice || property.type === PropertyType.MultipleChoice) {
      return (opts.choices ?? []).length > 0;
    }
    if (property.type === PropertyType.Tags) {
      return (opts.tags ?? []).length > 0;
    }
    if (property.type === PropertyType.Multilevel) {
      return (opts.data ?? []).length > 0;
    }
    return false;
  })();

  const handleEditPropertyConfig = () => {
    createPortal(PropertyModal, {
      value: {
        id: property.id,
        name: property.name,
        type: property.type,
        options: property.options,
      },
      onSaved: async (savedProperty: IProperty) => {
        // Reload full property data to get updated options
        if (property.pool === PropertyPool.Custom) {
          try {
            const rsp = await BApi.customProperty.getAllCustomProperties();
            const allProps = (rsp.data || []) as IProperty[];
            const updated = allProps.find((p) => p.id === property.id);
            if (updated) {
              setProperty(updated);
              setEditorKey((k) => k + 1);
            }
          } catch {
            // Fallback: use the saved property directly
            setProperty({ ...property, ...savedProperty });
            setEditorKey((k) => k + 1);
          }
        }
      },
    });
  };

  return (
    <Modal
      defaultVisible
      size="lg"
      title={
        <div className={"flex items-center gap-2"}>
          <span>{title ?? property.name}</span>
          {isReference && (
            <Button
              size="sm"
              variant="light"
              startContent={<SettingOutlined />}
              onPress={handleEditPropertyConfig}
            >
              {t<string>("propertyValueEditor.editPropertyConfig")}
            </Button>
          )}
        </div>
      }
      onDestroyed={onDestroyed}
      onOk={async () => {
        log("onOk clicked, selectedDbValue:", selectedDbValue, typeof selectedDbValue);
        if (selectedDbValue != null) {
          log("calling onSubmit with:", selectedDbValue);
          onSubmit(selectedDbValue);
        } else {
          log("selectedDbValue is null, not submitting");
        }
      }}
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: selectedDbValue == null,
        },
      }}
    >
      <div className={"flex flex-col gap-3"}>
        {/* No options hint for reference types */}
        {isReference && !hasOptions && (
          <div className={"text-sm text-default-400"}>
            {t<string>("propertyValueEditor.noOptions")}
          </div>
        )}

        {/* Value editor - isEditing locks it in edit mode (no blur exit) */}
        <PropertyValueRenderer
          key={editorKey}
          property={property}
          dbValue={selectedDbValue ?? undefined}
          size="sm"
          isEditing
          onValueChange={(dbValue) => {
            log("onValueChange dbValue:", JSON.stringify(dbValue));
            setSelectedDbValue(dbValue ?? null);
          }}
        />
      </div>
    </Modal>
  );
};

PropertyValueEditorModal.displayName = "PropertyValueEditorModal";

export default PropertyValueEditorModal;
