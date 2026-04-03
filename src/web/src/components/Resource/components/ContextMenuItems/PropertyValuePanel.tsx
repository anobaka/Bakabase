"use client";

/**
 * PropertyValuePanel - Panel rendered inside SubMenu for quick property value assignment.
 *
 * Unified pattern for ALL property types:
 * - Scrollable list of preset dbValues (click to apply)
 * - "Add preset value" button at the bottom (opens PropertyValueEditorModal)
 * - New values are both applied AND added to presets via onAddPreset callback
 */

import React from "react";
import { MenuItem, MenuDivider } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";
import { CloseOutlined, PlusOutlined } from "@ant-design/icons";

import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import PropertyValueEditorModal from "@/components/PropertyValueEditorModal";
import type { IProperty } from "@/components/Property/models";

type Props = {
  property: IProperty;
  /** Preset dbValues */
  presetValues: string[];
  /** Called with a dbValue when user clicks a preset to apply it */
  onApply: (dbValue: string) => void;
  /** Called when user adds a new preset value via the editor modal */
  onAddPreset?: (dbValue: string) => void;
  /** Called when user removes a preset value */
  onRemovePreset?: (dbValue: string) => void;
};

/** Non-interactive menu item wrapper */
const MenuItemContent = ({ children }: { children: React.ReactNode }) => (
  <li
    className="szh-menu__item"
    role="menuitem"
    onClick={(e) => e.stopPropagation()}
    onKeyDown={(e) => e.stopPropagation()}
    style={{ cursor: "default" }}
  >
    {children}
  </li>
);

const PropertyValuePanel = ({ property, presetValues, onApply, onAddPreset, onRemovePreset }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const handleAdd = () => {
    createPortal(PropertyValueEditorModal, {
      property,
      title: t<string>("resource.display.contextMenu.addPresetValueTitle", { property: property.name }),
      onSubmit: (dbValue: string) => {
        onApply(dbValue);
        onAddPreset?.(dbValue);
      },
    });
  };

  return (
    <>
      {/* Preset values list with max height */}
      {presetValues.length > 0 ? (
        <div style={{ maxHeight: "300px", overflowY: "auto" }}>
          {presetValues.map((dbVal, idx) => (
            <MenuItem key={`${dbVal}-${idx}`} onClick={() => onApply(dbVal)}>
              <div className={"flex items-center justify-between w-full gap-2"}>
                <PropertyValueRenderer
                  property={property}
                  dbValue={dbVal}
                  size="sm"
                  isReadonly
                />
                {onRemovePreset && (
                  <Button
                    isIconOnly
                    size="sm"
                    variant="light"
                    color="danger"
                    onPress={(e) => {
                      onRemovePreset(dbVal);
                    }}
                    onClick={(e) => e.stopPropagation()}
                  >
                    <CloseOutlined />
                  </Button>
                )}
              </div>
            </MenuItem>
          ))}
        </div>
      ) : (
        <MenuItem disabled>
          <span className={"text-default-400"}>{t("resource.contextMenu.noData")}</span>
        </MenuItem>
      )}

      <MenuDivider />

      {/* Add preset value button */}
      <MenuItemContent>
        <Button
          size="sm"
          variant="light"
          color="primary"
          startContent={<PlusOutlined />}
          onPress={handleAdd}
          className="w-full"
        >
          {t<string>("resource.display.contextMenu.addPresetValueFull")}
        </Button>
      </MenuItemContent>
    </>
  );
};

PropertyValuePanel.displayName = "PropertyValuePanel";

export default PropertyValuePanel;
