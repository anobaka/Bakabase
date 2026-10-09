"use client";

import type { IProperty } from "@/components/Property/models";

import { MenuItem, SubMenu, MenuDivider, MenuHeader } from "@szhsin/react-menu";
import { useTranslation } from "react-i18next";
import {
  ApiOutlined,
  DeleteOutlined,
  EditOutlined,
  ExportOutlined,
  FireOutlined,
  FolderOpenOutlined,
  PushpinOutlined,
  ReloadOutlined,
  RightOutlined,
  SendOutlined,
  SettingOutlined,
  ThunderboltOutlined,
  VideoCameraAddOutlined,
} from "@ant-design/icons";
import { HiOutlineCollection } from "react-icons/hi";
import React, { useCallback, useEffect, useState } from "react";

import PropertyValuePanel from "./PropertyValuePanel";
import BatchPlayMenuItems from "./BatchPlayMenuItems";

import MediaLibraryMultiSelector from "@/components/MediaLibraryMultiSelector";
import CollectionMultiSelector from "@/components/CollectionMultiSelector";
import { EnhancementAdditionalItem, PropertyPool, ResourceAdditionalItem } from "@/sdk/constants";
import ResourceTransferModal from "@/components/ResourceTransferModal";
import { openMovePanel } from "@/stores/resourceMovePanel";
import { getSelectionMoveBlockReasons } from "@/components/Resource/resourceMoveEligibility";
import { useMoveReasonText } from "@/components/ResourceMovePanel/messages";
import { snapshotMoveSelection } from "@/pages/resource/components/Resources/resourceMoveSelection";
import ResourceEnhancementsModal from "@/components/Resource/components/ResourceEnhancementsModal";
import { PlaylistCollection } from "@/components/Playlist";
import { Modal, Tooltip, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import BulkPropertyEditor from "@/components/Resource/components/BulkPropertyEditor";
import DeleteResourceConfirmContent from "@/components/Resource/components/DeleteResourceConfirmContent";
import { useUiOptionsStore } from "@/stores/options";
import { useOpenResourceDirectory } from "@/hooks/useOpenResourceDirectory";

type Props = {
  selectedResourceIds: number[];
  selectedResources?: any[];
  moveResourceIds?: number[];
  sourceTabId?: string;
  sourceTabName?: string;
  /** The resource whose context menu was triggered (may not be in selectedResources). */
  contextResource?: any;
  onSelectedResourcesChanged?: (ids: number[]) => any;
  /** Called after resources are deleted so the parent can prune them from the list. */
  onResourcesDeleted?: (ids: number[]) => any;
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

  const menuLabel = t<string>("resource.contextMenu.setPropertyValue", { property: property.name });

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
      setDownOverflow
      label={
        <div className={"flex items-center gap-2"}>
          <SettingOutlined aria-hidden className={"text-base"} />
          <span className="resource-context-menu__label">{menuLabel}</span>
          <RightOutlined aria-hidden className="resource-context-menu__chevron" />
        </div>
      }
      menuClassName="resource-context-menu resource-context-menu--submenu"
      menuStyle={{ maxHeight: "400px", minWidth: "240px" }}
      overflow="auto"
      submenuCloseDelay={150}
      submenuOpenDelay={0}
    >
      <PropertyValuePanel
        presetValues={presetValues}
        property={property}
        onAddPreset={onAddPreset}
        onApply={handleApply}
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
  contextResource,
  moveResourceIds = selectedResourceIds,
  sourceTabId,
  sourceTabName,
  onSelectedResourcesChanged,
  onResourcesDeleted,
}: Props) => {
  const { t } = useTranslation();
  const moveReasonText = useMoveReasonText();
  const moveResources = [
    ...(selectedResources ?? []),
    ...(contextResource ? [contextResource] : []),
  ];
  const moveDisabledReason = getSelectionMoveBlockReasons(moveResourceIds, moveResources)
    .map(moveReasonText)
    .join(" ");
  const { createPortal } = useBakabaseContext();
  const folderAction = useOpenResourceDirectory();
  const uiOptionsStore = useUiOptionsStore();
  const customContextMenuItems = uiOptionsStore.data?.resource?.customContextMenuItems ?? [];
  const resourceName = contextResource?.displayName || contextResource?.fileName;

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
      <MenuHeader className="resource-context-menu__summary">
        <span className="resource-context-menu__name" title={resourceName}>
          {resourceName || t("resource.contextMenu.title")}
        </span>
        <span className="resource-context-menu__count">
          {t("resource.contextMenu.selectionCount", { count: selectedResourceIds.length })}
        </span>
      </MenuHeader>
      <MenuDivider />
      {/* Custom property quick-set items */}
      {customContextMenuItems.map((item: any, itemIndex: number) => {
        const pool = item.property?.pool;
        const propId = item.property?.id;
        const property = propertyMap[pool]?.[propId];

        if (!property) return null;

        return (
          <PropertyQuickSetItem
            key={`${pool}-${propId}`}
            presetValues={item.presetValues ?? []}
            property={property}
            selectedResourceIds={selectedResourceIds}
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
            onSelectedResourcesChanged={onSelectedResourcesChanged}
          />
        );
      })}

      {customContextMenuItems.length > 0 && <MenuDivider />}

      {/* Open folder — always uses the right-clicked resource, independent of selection. */}
      {contextResource?.path && (
        <MenuItem onClick={() => folderAction.open(contextResource)}>
          <div className="flex items-center gap-2">
            <FolderOpenOutlined aria-hidden className="text-base" />
            <span className="resource-context-menu__label">{folderAction.label}</span>
            {selectedResourceIds.length > 1 && (
              <span className="resource-context-menu__annotation">
                {t("resource.contextMenu.currentOnly")}
              </span>
            )}
          </div>
        </MenuItem>
      )}

      {/* Single-resource actions (mirrored from cover buttons) */}
      {selectedResourceIds.length === 1 &&
        (() => {
          const resId = selectedResourceIds[0];
          const res = selectedResources?.[0];

          return (
            <>
              <MenuItem
                onClick={() => {
                  BApi.resource
                    .pinResource(resId, { pin: !res?.pinned })
                    .then(() => onSelectedResourcesChanged?.(selectedResourceIds));
                }}
              >
                <div className="flex items-center gap-2">
                  <PushpinOutlined aria-hidden className="text-base" />
                  {res?.pinned
                    ? t<string>("resource.operation.unpin")
                    : t<string>("resource.operation.pin")}
                </div>
              </MenuItem>
              <MenuItem
                onClick={() => {
                  BApi.resource
                    .getResourceEnhancements(resId, {
                      additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
                    })
                    .then((resp) => {
                      createPortal(ResourceEnhancementsModal, {
                        resourceId: resId,
                        enhancements: resp.data || [],
                      });
                    });
                }}
              >
                <div className="flex items-center gap-2">
                  <FireOutlined aria-hidden className="text-base" />
                  {t<string>("resource.operation.enhancements")}
                </div>
              </MenuItem>
              <MenuItem
                onClick={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t<string>("resource.operation.addToPlaylist"),
                    children: <PlaylistCollection addingResourceId={resId} />,
                    style: { minWidth: 600 },
                    footer: { actions: ["cancel"] },
                  });
                }}
              >
                <div className="flex items-center gap-2">
                  <VideoCameraAddOutlined aria-hidden className="text-base" />
                  {t<string>("resource.operation.addToPlaylist")}
                </div>
              </MenuItem>
              {(res?.dataStates?.length ?? 0) > 0 && (
                <MenuItem
                  onClick={async () => {
                    const rsp = await BApi.cache.refreshResourceCache(resId);

                    if (!rsp.code) {
                      toast.success(t<string>("resource.action.refreshCache.success"));
                      onSelectedResourcesChanged?.(selectedResourceIds);
                    }
                  }}
                >
                  <div className="flex items-center gap-2">
                    <ReloadOutlined aria-hidden className="text-base" />
                    {t<string>("resource.action.refreshCache")}
                  </div>
                </MenuItem>
              )}
              <MenuDivider />
            </>
          );
        })()}

      {/* Built-in menu items */}
      <BatchPlayMenuItems selectedResourceIds={selectedResourceIds} />
      {selectedResourceIds.length > 1 && (
        <MenuItem
          onClick={async () => {
            // Runs as a background task server-side; a large selection would otherwise
            // block while every resource is rescanned and its thumbnails regenerated.
            const rsp = await BApi.cache.refreshResourcesCache({ ids: selectedResourceIds });

            if (!rsp.code) {
              toast.success(t<string>("resource.action.refreshCache.taskStarted"));
            }
          }}
        >
          <div className="flex items-center gap-2">
            <ReloadOutlined aria-hidden className="text-base" />
            {t<string>("resource.contextMenu.refreshCacheForCount", {
              count: selectedResourceIds.length,
            })}
          </div>
        </MenuItem>
      )}
      <MenuHeader className="resource-context-menu__section">
        {t("resource.contextMenu.organizeSection")}
      </MenuHeader>
      <MenuItem
        onClick={() => {
          createPortal(MediaLibraryMultiSelector, {
            resourceIds: selectedResourceIds,
            onSubmit: () => onSelectedResourcesChanged?.(selectedResourceIds),
          });
        }}
      >
        <div className={"flex items-center gap-2"}>
          <ApiOutlined aria-hidden className={"text-base"} />
          {t<string>("resource.contextMenu.setMediaLibraries")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          createPortal(CollectionMultiSelector, {
            resourceIds: selectedResourceIds,
            onSubmit: () => onSelectedResourcesChanged?.(selectedResourceIds),
          });
        }}
      >
        <div className={"flex items-center gap-2"}>
          <HiOutlineCollection aria-hidden className={"text-base"} />
          {t<string>("resource.contextMenu.addToCollections")}
        </div>
      </MenuItem>
      <MenuItem
        onClick={() => {
          if (selectedResources && selectedResources.length > 0) {
            createPortal(ResourceTransferModal, { fromResources: selectedResources });
          } else {
            BApi.resource
              .getResourcesByKeys({
                ids: selectedResourceIds,
                additionalItems: ResourceAdditionalItem.All,
              })
              .then((r) => createPortal(ResourceTransferModal, { fromResources: r.data || [] }));
          }
        }}
      >
        <div className={"flex items-center gap-2"}>
          <SendOutlined aria-hidden className={"text-base"} />
          {t<string>("resource.contextMenu.transferResourceData")}
        </div>
      </MenuItem>
      <MenuItem
        disabled={!!moveDisabledReason}
        onClick={() => {
          if (moveDisabledReason) return;
          openMovePanel({
            resources: snapshotMoveSelection(moveResourceIds, moveResources),
            sourceTabId,
            sourceTabName,
          });
        }}
      >
        <Tooltip content={moveDisabledReason} isDisabled={!moveDisabledReason}>
          <div className="flex flex-col gap-1 max-w-72" title={moveDisabledReason || undefined}>
            <div className={"flex items-center gap-2"}>
              <ExportOutlined aria-hidden className={"text-base"} />
              {moveResourceIds.length > 1
                ? t<string>("resource.contextMenu.moveCountResources", {
                    count: moveResourceIds.length,
                  })
                : t<string>("resource.contextMenu.moveResource")}
            </div>
            {moveDisabledReason && (
              <span className="text-xs whitespace-normal">{moveDisabledReason}</span>
            )}
          </div>
        </Tooltip>
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
        <div className={"flex items-center gap-2"}>
          <EditOutlined aria-hidden className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("resource.contextMenu.bulkEditProperties")
            : t<string>("resource.contextMenu.editProperties")}
        </div>
      </MenuItem>
      <MenuDivider />
      <MenuHeader className="resource-context-menu__section">
        {t("resource.contextMenu.maintenanceSection")}
      </MenuHeader>
      <MenuItem
        onClick={() => {
          const count = selectedResourceIds.length;

          createPortal(Modal, {
            defaultVisible: true,
            title:
              count > 1
                ? t<string>("resource.contextMenu.reEnhanceCount", { count })
                : t<string>("resource.contextMenu.reEnhance"),
            children: (
              <div className={"text-sm text-default-700"}>
                {t<string>("resource.contextMenu.reEnhance.confirm", { count })}
              </div>
            ),
            footer: { actions: ["cancel", "ok"] },
            onOk: async () => {
              const rsp = await BApi.resources.deleteEnhancementsByResources(selectedResourceIds);

              if (!rsp.code) {
                toast.success(t<string>("resource.contextMenu.reEnhance.scheduled"));
                onSelectedResourcesChanged?.(selectedResourceIds);
              }
            },
          });
        }}
      >
        <Tooltip content={t<string>("resource.contextMenu.reEnhance.tooltip")} placement={"right"}>
          <div className={"flex items-center gap-2"}>
            <ThunderboltOutlined aria-hidden className={"text-base"} />
            {selectedResourceIds.length > 1
              ? t<string>("resource.contextMenu.reEnhanceCount", {
                  count: selectedResourceIds.length,
                })
              : t<string>("resource.contextMenu.reEnhance")}
          </div>
        </Tooltip>
      </MenuItem>
      <MenuDivider />
      <MenuItem
        className="resource-context-menu__danger"
        onClick={() => {
          let deleteFiles = false;

          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("resource.contextMenu.deleteCountResources", {
              count: selectedResourceIds.length,
            }),
            children: (
              <DeleteResourceConfirmContent
                count={selectedResourceIds.length}
                onDeleteFilesChange={(v) => {
                  deleteFiles = v;
                }}
              />
            ),
            footer: {
              actions: ["cancel", "ok"],
              okProps: { color: "danger", children: t<string>("common.action.delete") },
            },
            onOk: async () => {
              await BApi.resource.bulkDeleteResources({ ids: selectedResourceIds, deleteFiles });
              onResourcesDeleted?.(selectedResourceIds);
            },
          });
        }}
      >
        <div className={"flex items-center gap-2"}>
          <DeleteOutlined aria-hidden className={"text-base"} />
          {selectedResourceIds.length > 1
            ? t<string>("resource.contextMenu.deleteCountResources", {
                count: selectedResourceIds.length,
              })
            : t<string>("resource.contextMenu.deleteResource")}
        </div>
      </MenuItem>
    </>
  );
};

ContextMenuItems.displayName = "ContextMenuItems";

export default ContextMenuItems;
