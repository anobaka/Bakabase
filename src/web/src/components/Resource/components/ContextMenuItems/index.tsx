"use client";

import { MenuItem, SubMenu, MenuDivider } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";
import {
  ApiOutlined,
  DeleteOutlined,
  EditOutlined,
  SendOutlined,
  SettingOutlined,
} from "@ant-design/icons";
import React, { useCallback, useEffect, useState } from "react";

import MediaLibraryMultiSelector from "@/components/MediaLibraryMultiSelector";
import { PropertyPool, ResourceAdditionalItem } from "@/sdk/constants";
import ResourceTransferModal from "@/components/ResourceTransferModal";
import { Modal, toast } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import BulkPropertyEditor from "@/components/Resource/components/BulkPropertyEditor";
import DeleteResourceConfirmContent from "@/components/Resource/components/DeleteResourceConfirmContent";
import { useUiOptionsStore } from "@/stores/options";
import type { IProperty } from "@/components/Property/models";
import PropertyValuePanel from "./PropertyValuePanel";

const log = buildLogger("ResourceContextMenuItems");

type Props = {
  selectedResourceIds: number[];
  selectedResources?: any[];
  onSelectedResourcesChanged?: (ids: number[]) => any;
};

// ============================================================================
// Custom property quick-set SubMenu item
// ============================================================================

const PropertyQuickSetItem = ({
  property,
  presetValues,
  selectedResourceIds,
  onSelectedResourcesChanged,
  onAddPreset,
  onRemovePreset,
}: {
  property: IProperty;
  presetValues: string[];
  selectedResourceIds: number[];
  onSelectedResourcesChanged?: (ids: number[]) => any;
  onAddPreset?: (dbValue: string) => void;
  onRemovePreset?: (dbValue: string) => void;
}) => {
  const { t } = useTranslation();

  const menuLabel =
    selectedResourceIds.length > 1
      ? t<string>("resource.contextMenu.setPropertyValueForCount", {
          property: property.name,
          count: selectedResourceIds.length,
        })
      : t<string>("resource.contextMenu.setPropertyValue", { property: property.name });

  const handleApply = useCallback(
    async (dbValue: string) => {
      try {
        const rsp = await BApi.resource.bulkPutResourcePropertyValue({
          resourceIds: selectedResourceIds,
          propertyId: property.id,
          isCustomProperty: property.pool === PropertyPool.Custom,
          value: dbValue,
        });
        if (!rsp.code) {
          toast.success(t("resource.contextMenu.propertyValueSet"));
          onSelectedResourcesChanged?.(selectedResourceIds);
        }
      } catch {
        toast.danger(t("resource.contextMenu.propertyValueSetFailed"));
      }
    },
    [selectedResourceIds, property, onSelectedResourcesChanged, t],
  );

  return (
    <SubMenu
      label={
        <div className={"flex items-center gap-2"}>
          <SettingOutlined className={"text-base"} />
          {menuLabel}
        </div>
      }
      overflow="auto"
      setDownOverflow
      menuStyle={{ maxHeight: "400px", minWidth: "240px" }}
      submenuOpenDelay={0}
      submenuCloseDelay={150}
    >
      <PropertyValuePanel
        property={property}
        presetValues={presetValues}
        onApply={handleApply}
        onAddPreset={onAddPreset}
        onRemovePreset={onRemovePreset}
      />
    </SubMenu>
  );
};

// ============================================================================
// Main ContextMenuItems
// ============================================================================

const ContextMenuItems = ({
  selectedResourceIds,
  selectedResources,
  onSelectedResourcesChanged,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const uiOptionsStore = useUiOptionsStore();
  const customContextMenuItems = uiOptionsStore.data?.resource?.customContextMenuItems ?? [];

  const [propertyMap, setPropertyMap] = useState<Record<number, Record<number, IProperty>>>({});

  useEffect(() => {
    if (customContextMenuItems.length === 0) return;

    const loadProperties = async () => {
      const [builtinPropsRsp, customPropsRsp] = await Promise.all([
        // @ts-ignore
        BApi.property.getPropertiesByPool(PropertyPool.Reserved | PropertyPool.Internal),
        BApi.customProperty.getAllCustomProperties(),
      ]);

      const builtinProps = (builtinPropsRsp.data || []) as IProperty[];
      const customProps = (customPropsRsp.data || []) as IProperty[];

      const ps: Record<number, Record<number, IProperty>> = {};
      for (const p of builtinProps) {
        if (!ps[p.pool]) ps[p.pool] = {};
        ps[p.pool][p.id] = p;
      }
      for (const p of customProps) {
        if (!ps[PropertyPool.Custom]) ps[PropertyPool.Custom] = {};
        ps[PropertyPool.Custom][p.id] = p;
      }
      setPropertyMap(ps);
    };

    loadProperties();
  }, [customContextMenuItems.length]);

  return (
    <>
      {/* Custom property quick-set items */}
      {customContextMenuItems.map((item: any, itemIndex: number) => {
        const pool = item.property?.pool;
        const propId = item.property?.id;
        const property = propertyMap[pool]?.[propId];
        if (!property) return null;

        return (
          <PropertyQuickSetItem
            key={`${pool}-${propId}`}
            property={property}
            presetValues={item.presetValues ?? []}
            selectedResourceIds={selectedResourceIds}
            onSelectedResourcesChanged={onSelectedResourcesChanged}
            onAddPreset={(dbValue) => {
              const currentPresets = item.presetValues ?? [];
              if (!currentPresets.includes(dbValue)) {
                const newItems = [...customContextMenuItems];
                newItems[itemIndex] = {
                  ...newItems[itemIndex],
                  presetValues: [...currentPresets, dbValue],
                };
                uiOptionsStore.patch({
                  resource: {
                    ...uiOptionsStore.data?.resource,
                    customContextMenuItems: newItems,
                  },
                });
              }
            }}
            onRemovePreset={(dbValue) => {
              const newItems = [...customContextMenuItems];
              newItems[itemIndex] = {
                ...newItems[itemIndex],
                presetValues: (item.presetValues ?? []).filter((v: string) => v !== dbValue),
              };
              uiOptionsStore.patch({
                resource: {
                  ...uiOptionsStore.data?.resource,
                  customContextMenuItems: newItems,
                },
              });
            }}
          />
        );
      })}

      {customContextMenuItems.length > 0 && <MenuDivider />}

      {/* Built-in menu items */}
      <MenuItem
        onClick={() => {
          createPortal(MediaLibraryMultiSelector, {
            resourceIds: selectedResourceIds,
            onSubmit: () => onSelectedResourcesChanged?.(selectedResourceIds),
          });
        }}
      >
        <div className={"flex items-center gap-2"}>
          <ApiOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("resource.contextMenu.setMediaLibrariesForCount", { count: selectedResourceIds.length })
            : t<string>("resource.contextMenu.setMediaLibraries")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          if (selectedResources && selectedResources.length > 0) {
            createPortal(ResourceTransferModal, { fromResources: selectedResources });
          } else {
            BApi.resource
              .getResourcesByKeys({ ids: selectedResourceIds, additionalItems: ResourceAdditionalItem.All })
              .then((r) => createPortal(ResourceTransferModal, { fromResources: r.data || [] }));
          }
        }}
      >
        <div className={"flex items-center gap-2"}>
          <SendOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("resource.contextMenu.transferDataOfCount", { count: selectedResourceIds.length })
            : t<string>("resource.contextMenu.transferResourceData")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          createPortal(BulkPropertyEditor, {
            resourceIds: selectedResourceIds,
            initialResources: selectedResources,
            onSubmitted: () => onSelectedResourcesChanged?.(selectedResourceIds),
          });
        }}
      >
        <div className={"flex items-center gap-2 text-secondary"}>
          <EditOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("resource.contextMenu.bulkEditProperties")
            : t<string>("resource.contextMenu.editProperties")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          let deleteFiles = false;
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("resource.contextMenu.deleteCountResources", { count: selectedResourceIds.length }),
            children: (
              <DeleteResourceConfirmContent
                count={selectedResourceIds.length}
                onDeleteFilesChange={(v) => { deleteFiles = v; }}
              />
            ),
            onOk: async () => {
              await BApi.resource.deleteResourcesByKeys({ ids: selectedResourceIds, deleteFiles });
            },
          });
        }}
      >
        <div className={"flex items-center gap-2 text-danger"}>
          <DeleteOutlined className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("resource.contextMenu.deleteCountResources", { count: selectedResourceIds.length })
            : t<string>("resource.contextMenu.deleteResource")}
        </div>
      </MenuItem>
    </>
  );
};

ContextMenuItems.displayName = "ContextMenuItems";

export default ContextMenuItems;
