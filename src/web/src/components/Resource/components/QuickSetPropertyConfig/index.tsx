"use client";

/**
 * QuickSetPropertyConfig - standalone component for configuring
 * which properties appear in the right-click "Quick Set Property" context menu,
 * and their preset values. Used in both the display options modal and the test page.
 */

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  CloseOutlined,
  DeleteOutlined,
  PlusOutlined,
} from "@ant-design/icons";
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";

import { Button, Checkbox, Chip } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertySelector from "@/components/PropertySelector";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import type { IProperty } from "@/components/Property/models";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import DragHandle from "@/components/DragHandle";
import PropertyValueEditorModal from "@/components/PropertyValueEditorModal";

// ============================================================================
// Types
// ============================================================================

export type CustomContextMenuItem = {
  property: { pool: number; id: number };
  presetValues: string[];
};

type Props = {
  items: CustomContextMenuItem[];
  onChange: (items: CustomContextMenuItem[]) => void | Promise<void>;
  autoAddRecentPropertyValues?: boolean;
  onAutoAddChange?: (value: boolean) => void | Promise<void>;
};

// ============================================================================
// Sortable Item
// ============================================================================

const SortablePropertyItem = ({ item, property, onRemove, onPresetChange, onReloadProperties }: {
  item: CustomContextMenuItem;
  property: IProperty | undefined;
  onRemove: () => void;
  onPresetChange: (presetValues: string[]) => void;
  onReloadProperties: () => void;
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const presetValues = item.presetValues ?? [];

  const itemId = `${item.property.pool}-${item.property.id}`;
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: itemId });
  const style = { transform: CSS.Transform.toString(transform), transition, opacity: isDragging ? 0.5 : 1 };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={"rounded-lg p-3 flex flex-col gap-2 hover:bg-default-100 transition-colors"}
    >
      {/* Row 1: Drag handle + property chip + delete */}
      <div className={"flex items-center gap-2"}>
        <DragHandle {...listeners} {...attributes} />
        <div className={"flex items-center gap-1 shrink-0"}>
          {property ? (
            <BriefProperty property={property} fields={["pool", "name"]} />
          ) : (
            <Chip>{t("common.label.unknownProperty")}</Chip>
          )}
        </div>
        <Button
          isIconOnly
          color="danger"
          size="sm"
          variant="light"
          onPress={onRemove}
          className="ml-auto shrink-0"
        >
          <DeleteOutlined />
        </Button>
      </div>

      {/* Row 2: Preset values */}
      <div className={"flex flex-col gap-1 pl-8"}>
        <div className={"text-xs text-default-500"}>
          {t<string>("resource.display.contextMenu.presetValues")}
          {presetValues.length > 0 && ` (${presetValues.length})`}
        </div>

        {/* Existing preset values */}
        {presetValues.length > 0 && (
          <div className={"flex flex-wrap items-center gap-1"}>
            {presetValues.map((bv, valIdx) => (
              <div key={valIdx} className={"flex items-center gap-1"}>
                {property && (
                  <PropertyValueRenderer
                    property={property}
                    dbValue={bv}
                    size="sm"
                    isReadonly
                  />
                )}
                <Button
                  isIconOnly
                  color="danger"
                  size="sm"
                  variant="light"
                  onPress={() => {
                    onPresetChange(presetValues.filter((_, i) => i !== valIdx));
                  }}
                >
                  <CloseOutlined />
                </Button>
              </div>
            ))}
          </div>
        )}

        {/* Add preset value button */}
        {property && (
          <div className={"flex flex-col gap-0.5"}>
            <Button
              size="sm"
              variant="flat"
              startContent={<PlusOutlined />}
              className="self-start"
              onPress={() => {
                createPortal(PropertyValueEditorModal, {
                  property,
                  title: t<string>("resource.display.contextMenu.addPresetValueTitle", { property: property.name }),
                  onSubmit: (dbValue: string) => {
                    if (dbValue && !presetValues.includes(dbValue)) {
                      onPresetChange([...presetValues, dbValue]);
                    }
                  },
                  onDestroyed: () => {
                    onReloadProperties();
                  },
                });
              }}
            >
              {t<string>("resource.display.contextMenu.addPresetValueFull")}
            </Button>
            <div className={"text-xs text-default-400"}>
              {t<string>("resource.display.contextMenu.addPresetValueHint")}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

// ============================================================================
// Main Component
// ============================================================================

const QuickSetPropertyConfig = ({ items, onChange, autoAddRecentPropertyValues, onAutoAddChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [propertyMap, setPropertyMap] = useState<Record<number, Record<number, IProperty>>>({});

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const loadProperties = async () => {
    const [builtinRsp, customRsp] = await Promise.all([
      // @ts-ignore
      BApi.property.getPropertiesByPool(PropertyPool.Reserved | PropertyPool.Internal),
      BApi.customProperty.getAllCustomProperties(),
    ]);
    const ps: Record<number, Record<number, IProperty>> = {};
    for (const p of (builtinRsp.data || []) as IProperty[]) {
      if (!ps[p.pool]) ps[p.pool] = {};
      ps[p.pool][p.id] = p;
    }
    for (const p of (customRsp.data || []) as IProperty[]) {
      if (!ps[PropertyPool.Custom]) ps[PropertyPool.Custom] = {};
      ps[PropertyPool.Custom][p.id] = p;
    }
    setPropertyMap(ps);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const sortableIds = items.map((item) => `${item.property.pool}-${item.property.id}`);

  const handleAddProperty = () => {
    createPortal(PropertySelector, {
      v2: true,
      multiple: true,
      pool: PropertyPool.Reserved | PropertyPool.Custom,
      selection: items.map((item) => ({
        id: item.property.id,
        pool: item.property.pool,
      })),
      onSubmit: async (selected: any[]) => {
        const newItems = selected.map((prop: any) => {
          const key = `${prop.pool}-${prop.id}`;
          const existing = items.find(
            (item) => `${item.property.pool}-${item.property.id}` === key,
          );
          return existing ?? { property: { pool: prop.pool, id: prop.id }, presetValues: [] };
        });
        await onChange(newItems);
      },
    });
  };

  const handleDragEnd = async (event: any) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = sortableIds.indexOf(active.id);
    const newIndex = sortableIds.indexOf(over.id);
    if (oldIndex === -1 || newIndex === -1) return;
    await onChange(arrayMove([...items], oldIndex, newIndex));
  };

  return (
    <div className={"flex flex-col gap-4"}>
      <div className={"flex flex-col gap-3"}>
        <div>
          <div className={"text-sm font-medium"}>
            {t<string>("resource.display.contextMenu.quickSetProperty")}
          </div>
          <div className={"text-xs text-default-400 mt-1"}>
            {t<string>("resource.display.contextMenu.quickSetPropertyDesc")}
          </div>
        </div>

        {onAutoAddChange && (
          <Checkbox
            size="sm"
            isSelected={!!autoAddRecentPropertyValues}
            onValueChange={(checked) => onAutoAddChange(checked)}
          >
            {t<string>("resource.display.contextMenu.autoAddRecent")}
          </Checkbox>
        )}

        {items.length === 0 ? (
          <div className={"text-sm text-default-400 text-center py-4"}>
            {t<string>("resource.display.contextMenu.noItems")}
          </div>
        ) : (
          <DndContext collisionDetection={closestCenter} sensors={sensors} onDragEnd={handleDragEnd}>
            <SortableContext items={sortableIds} strategy={verticalListSortingStrategy}>
              <div className={"flex flex-col gap-2"}>
                {items.map((item, itemIndex) => {
                  const pool = item.property.pool as PropertyPool;
                  const propId = item.property.id;
                  const property = propertyMap[pool]?.[propId] as IProperty | undefined;

                  return (
                    <SortablePropertyItem
                      key={`${pool}-${propId}`}
                      item={item}
                      property={property}
                      onRemove={async () => {
                        await onChange(items.filter((_, i) => i !== itemIndex));
                      }}
                      onPresetChange={async (presetValues) => {
                        const newItems = [...items];
                        newItems[itemIndex] = { ...newItems[itemIndex], presetValues };
                        await onChange(newItems);
                      }}
                      onReloadProperties={loadProperties}
                    />
                  );
                })}
              </div>
            </SortableContext>
          </DndContext>
        )}

        <Button
          size="sm"
          variant="flat"
          startContent={<PlusOutlined />}
          onPress={handleAddProperty}
          className="self-start"
        >
          {t<string>("resource.display.contextMenu.addProperty")}
        </Button>
      </div>
    </div>
  );
};

QuickSetPropertyConfig.displayName = "QuickSetPropertyConfig";

export default QuickSetPropertyConfig;
