"use client";

import type { PropertyType } from "@/sdk/constants";
import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useCallback, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import {
  VerticalAlignTopOutlined,
  VerticalAlignBottomOutlined,
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

import {
  Button,
  Input,
  Modal,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { PropertyLabel } from "@/components/Property";
import { PropertyPool } from "@/sdk/constants";
import DragHandle from "@/components/DragHandle";

type PropertyLike = {
  id: number;
  name: string;
  type: PropertyType;
};

type Props = {
  properties: PropertyLike[];
} & DestroyableProps;

interface SortableRowProps {
  item: PropertyLike;
  globalIndex: number;
  totalCount: number;
  isEditing: boolean;
  editingValue: string;
  onEditStart: (id: number, value: string) => void;
  onEditEnd: () => void;
  onEditingValueChange: (value: string) => void;
  onOrderInputKeyDown: (e: React.KeyboardEvent<HTMLInputElement>, itemId: number) => void;
  onOrderInputBlur: (itemId: number) => void;
  onMoveToTop: (itemId: number) => void;
  onMoveToBottom: (itemId: number) => void;
  t: (key: string) => string;
}

const SortableRow = ({
  item,
  globalIndex,
  totalCount,
  isEditing,
  editingValue,
  onEditStart,
  onEditEnd,
  onEditingValueChange,
  onOrderInputKeyDown,
  onOrderInputBlur,
  onMoveToTop,
  onMoveToBottom,
  t,
}: SortableRowProps) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: item.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  return (
    <div
      ref={setNodeRef}
      className="flex items-center gap-2 py-2 px-2 border-b border-default-200 hover:bg-default-100"
      style={style}
    >
      <DragHandle {...listeners} {...attributes} />
      <div className="w-16">
        {isEditing ? (
          <Input
            autoFocus
            className="w-16"
            size="sm"
            type="number"
            value={editingValue}
            onBlur={() => onOrderInputBlur(item.id)}
            onKeyDown={(e) => onOrderInputKeyDown(e, item.id)}
            onValueChange={onEditingValueChange}
          />
        ) : (
          <Tooltip content={t("Click to edit order")}>
            <div
              className="cursor-pointer px-2 py-1 rounded hover:bg-default-200 inline-block min-w-[40px] text-center"
              onClick={() => {
                onEditStart(item.id, String(globalIndex));
              }}
            >
              {globalIndex}
            </div>
          </Tooltip>
        )}
      </div>
      <div className="flex-1">
        <PropertyLabel
          property={{
            name: item.name,
            type: item.type,
            pool: PropertyPool.Custom,
          }}
        />
      </div>
      <div className="flex gap-1">
        <Tooltip content={t("Move to top")}>
          <Button
            isDisabled={globalIndex === 1}
            isIconOnly
            size="sm"
            variant="light"
            onPress={() => onMoveToTop(item.id)}
          >
            <VerticalAlignTopOutlined className="text-base" />
          </Button>
        </Tooltip>
        <Tooltip content={t("Move to bottom")}>
          <Button
            isDisabled={globalIndex === totalCount}
            isIconOnly
            size="sm"
            variant="light"
            onPress={() => onMoveToBottom(item.id)}
          >
            <VerticalAlignBottomOutlined className="text-base" />
          </Button>
        </Tooltip>
      </div>
    </div>
  );
};

const CustomPropertySortModal = ({ properties, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [items, setItems] = useState<PropertyLike[]>(properties);
  const [searchText, setSearchText] = useState("");
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editingValue, setEditingValue] = useState("");

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const filteredItems = useMemo(() => {
    if (!searchText.trim()) return items;
    const lowerSearch = searchText.toLowerCase();
    return items.filter((item) =>
      item.name.toLowerCase().includes(lowerSearch)
    );
  }, [items, searchText]);

  const saveOrder = useCallback(
    async (newItems: PropertyLike[]) => {
      const ids = newItems.map((item) => item.id);
      await BApi.customProperty.sortCustomProperties({ ids });
      toast.success(t<string>("Saved"));
    },
    [t]
  );

  const handleOrderChange = useCallback(
    async (itemId: number, newOrder: number) => {
      const currentIndex = items.findIndex((item) => item.id === itemId);
      if (currentIndex === -1) return;

      // Clamp to valid range (1-based)
      const targetIndex = Math.max(0, Math.min(items.length - 1, newOrder - 1));

      if (currentIndex === targetIndex) return;

      const newItems = [...items];
      const [removed] = newItems.splice(currentIndex, 1);
      newItems.splice(targetIndex, 0, removed);

      setItems(newItems);
      await saveOrder(newItems);
    },
    [items, saveOrder]
  );

  const handleMoveToTop = useCallback(
    async (itemId: number) => {
      const currentIndex = items.findIndex((item) => item.id === itemId);
      if (currentIndex <= 0) return;

      const newItems = [...items];
      const [removed] = newItems.splice(currentIndex, 1);
      newItems.unshift(removed);

      setItems(newItems);
      await saveOrder(newItems);
    },
    [items, saveOrder]
  );

  const handleMoveToBottom = useCallback(
    async (itemId: number) => {
      const currentIndex = items.findIndex((item) => item.id === itemId);
      if (currentIndex === -1 || currentIndex === items.length - 1) return;

      const newItems = [...items];
      const [removed] = newItems.splice(currentIndex, 1);
      newItems.push(removed);

      setItems(newItems);
      await saveOrder(newItems);
    },
    [items, saveOrder]
  );


  const handleOrderInputKeyDown = (
    e: React.KeyboardEvent<HTMLInputElement>,
    itemId: number
  ) => {
    if (e.key === "Enter") {
      const newOrder = parseInt(editingValue, 10);
      if (!isNaN(newOrder) && newOrder >= 1 && newOrder <= items.length) {
        handleOrderChange(itemId, newOrder);
      }
      setEditingId(null);
    } else if (e.key === "Escape") {
      setEditingId(null);
    }
  };

  const handleOrderInputBlur = (itemId: number) => {
    const newOrder = parseInt(editingValue, 10);
    if (!isNaN(newOrder) && newOrder >= 1 && newOrder <= items.length) {
      handleOrderChange(itemId, newOrder);
    }
    setEditingId(null);
  };

  const handleDragEnd = useCallback(
    async (event: any) => {
      const { active, over } = event;

      if (active.id !== over?.id) {
        const oldIndex = items.findIndex((item) => item.id === active.id);
        const newIndex = items.findIndex((item) => item.id === over?.id);

        if (oldIndex !== -1 && newIndex !== -1) {
          const newItems = arrayMove(items, oldIndex, newIndex);
          setItems(newItems);
          await saveOrder(newItems);
        }
      }
    },
    [items, saveOrder]
  );

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
        cancelProps: {
          text: t<string>("Close"),
        },
      }}
      size={"3xl"}
      title={t<string>("Adjust orders of properties")}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <div className="text-sm text-default-500">
            {t("Click on the order number to edit directly, or use the buttons to move to top/bottom")}
          </div>
          <Input
            className="w-64"
            placeholder={t<string>("Search properties...")}
            size="sm"
            value={searchText}
            onValueChange={setSearchText}
          />
        </div>

        <DndContext
          collisionDetection={closestCenter}
          sensors={sensors}
          onDragEnd={handleDragEnd}
        >
          <SortableContext
            items={filteredItems.map((item) => item.id)}
            strategy={verticalListSortingStrategy}
          >
            <div className="border border-default-200 rounded-lg">
              <div className="flex items-center gap-2 py-2 px-2 bg-default-100 font-medium border-b border-default-200">
                <div className="w-8" />
                <div className="w-16">{t("Order")}</div>
                <div className="flex-1">{t("Property")}</div>
                <div className="w-24">{t("Actions")}</div>
              </div>
              {filteredItems.length === 0 ? (
                <div className="py-8 text-center text-default-500">
                  {t<string>("No properties found")}
                </div>
              ) : (
                filteredItems.map((item, index) => {
                  const globalIndex = index + 1;
                  const isEditing = editingId === item.id;

                  return (
                    <SortableRow
                      key={item.id}
                      editingValue={editingValue}
                      globalIndex={globalIndex}
                      isEditing={isEditing}
                      item={item}
                      t={t}
                      totalCount={items.length}
                      onEditEnd={() => setEditingId(null)}
                      onEditStart={(id, value) => {
                        setEditingId(id);
                        setEditingValue(value);
                      }}
                      onEditingValueChange={setEditingValue}
                      onMoveToBottom={handleMoveToBottom}
                      onMoveToTop={handleMoveToTop}
                      onOrderInputBlur={handleOrderInputBlur}
                      onOrderInputKeyDown={handleOrderInputKeyDown}
                    />
                  );
                })
              )}
            </div>
          </SortableContext>
        </DndContext>
      </div>
    </Modal>
  );
};

CustomPropertySortModal.displayName = "CustomPropertySortModal";

export default CustomPropertySortModal;
