"use client";

import type { CSSProperties, ReactNode } from "react";
import type { DragEndEvent } from "@dnd-kit/core";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { useTranslation } from "react-i18next";
import { AutoSizer, List } from "react-virtualized";
import { EditOutlined, PlusOutlined, SortAscendingOutlined } from "@ant-design/icons";

import { moveReferenceValue, sortReferenceValues } from "./helpers";

import { Button, Popover, Textarea } from "@/components/bakaui";

type Item = { value: string };
interface Props<T extends Item> {
  items: T[];
  onChange?: (items: T[]) => void;
  className?: string;
  kind: "choices" | "tags";
  createItem: () => T;
  itemText: (item: T) => string;
  fromText: (items: T[], text: string) => T[];
  renderItem: (props: {
    item: T;
    compact: boolean;
    style: CSSProperties;
    onChange: (item: T) => void;
    onRemove: () => void;
    onAdd: () => void;
  }) => ReactNode;
}

export default function ReferenceListEditor<T extends Item>({
  items: initialItems,
  onChange,
  className,
  kind,
  createItem,
  itemText,
  fromText,
  renderItem,
}: Props<T>) {
  const { t } = useTranslation();
  const [items, setItems] = useState(initialItems);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkText, setBulkText] = useState("");
  const listRef = useRef<List>(null);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  const bulkItems = useMemo(() => fromText(items, bulkText), [items, bulkText, fromText]);
  const removedCount = items.filter((item) => !bulkItems.includes(item)).length;
  const addedCount = bulkItems.filter((item) => !items.includes(item)).length;

  useEffect(() => {
    onChange?.(items);
  }, [items]);

  const addItem = () => {
    setItems((previous) => [...previous, createItem()]);
    requestAnimationFrame(() => listRef.current?.scrollToRow(items.length));
  };

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    setItems((previous) => moveReferenceValue(previous, active.id, over?.id));
  };

  return (
    <div className={`flex min-w-0 flex-col gap-3 ${className ?? ""}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-sm font-medium">
            {t(`property.referenceEditor.${kind}.title`)}
            <span className="ml-2 text-xs font-normal tabular-nums text-default-400">
              {items.length}
            </span>
          </div>
          <p className="mt-1 text-xs leading-relaxed text-default-500">
            {t("property.referenceEditor.dragHelp")}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <Button
            color="primary"
            size="sm"
            startContent={<PlusOutlined />}
            variant="flat"
            onPress={addItem}
          >
            {t(`property.referenceEditor.${kind}.add`)}
          </Button>
          <Popover
            classNames={{ content: "max-w-[calc(100vw-2rem)]" }}
            placement="bottom-end"
            trigger={
              <Button size="sm" startContent={<EditOutlined />} variant="light">
                {t("property.referenceEditor.bulkEdit")}
              </Button>
            }
            visible={bulkOpen}
            onVisibleChange={(open) => {
              if (open) setBulkText(items.map(itemText).join("\n"));
              setBulkOpen(open);
            }}
          >
            <div className="flex w-[26rem] max-w-[calc(100vw-4rem)] flex-col gap-3 p-3">
              <div className="text-sm font-medium">{t("property.referenceEditor.bulkEdit")}</div>
              <p className="text-xs leading-relaxed text-default-500">
                {t(`property.referenceEditor.${kind}.bulkHelp`)}
              </p>
              <p className="rounded-lg bg-warning-50 p-2 text-xs leading-relaxed text-warning-700">
                {t("property.referenceEditor.bulkWarning")}
              </p>
              <Textarea
                aria-label={t(`property.referenceEditor.${kind}.title`)}
                maxRows={14}
                minRows={5}
                value={bulkText}
                onValueChange={setBulkText}
              />
              <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs tabular-nums">
                <span className="text-default-500">
                  {t("property.referenceEditor.addedCount", { count: addedCount })}
                </span>
                <span className={removedCount > 0 ? "text-danger" : "text-default-500"}>
                  {t("property.referenceEditor.removedCount", { count: removedCount })}
                </span>
              </div>
              <div className="flex justify-end gap-2">
                <Button size="sm" variant="light" onPress={() => setBulkOpen(false)}>
                  {t("common.action.cancel")}
                </Button>
                <Button
                  color="primary"
                  size="sm"
                  onPress={() => {
                    setItems(bulkItems);
                    setBulkOpen(false);
                  }}
                >
                  {t("common.action.apply")}
                </Button>
              </div>
            </div>
          </Popover>
          <Button
            isDisabled={items.length < 2}
            size="sm"
            startContent={<SortAscendingOutlined />}
            variant="light"
            onPress={() => setItems((previous) => sortReferenceValues(previous, itemText))}
          >
            {t("property.referenceEditor.sort")}
          </Button>
        </div>
      </div>
      {items.length === 0 ? (
        <div className="rounded-xl bg-default-50 px-4 py-6 text-center text-sm text-default-400">
          {t(`property.referenceEditor.${kind}.empty`)}
        </div>
      ) : (
        <DndContext collisionDetection={closestCenter} sensors={sensors} onDragEnd={onDragEnd}>
          <SortableContext
            items={items.map((item) => item.value)}
            strategy={verticalListSortingStrategy}
          >
            <div className="flex items-center justify-between gap-3 px-3 text-xs text-default-400">
              <span>{t(`property.referenceEditor.${kind}.columns`)}</span>
              <span className="shrink-0">{t("property.referenceEditor.usageActions")}</span>
            </div>
            <AutoSizer disableHeight>
              {({ width }) => {
                const compact = width < 560;
                const rowHeight = compact ? (kind === "tags" ? 120 : 86) : 58;

                return (
                  <List
                    ref={listRef}
                    className="outline-none"
                    height={Math.min(items.length, 6) * rowHeight}
                    rowCount={items.length}
                    rowHeight={rowHeight}
                    rowRenderer={({ index, style }) => {
                      const item = items[index]!;

                      return renderItem({
                        item,
                        compact,
                        style,
                        onAdd: addItem,
                        onChange: (next) =>
                          setItems((previous) =>
                            previous.map((entry) => (entry.value === item.value ? next : entry)),
                          ),
                        onRemove: () =>
                          setItems((previous) =>
                            previous.filter((entry) => entry.value !== item.value),
                          ),
                      });
                    }}
                    width={width}
                  />
                );
              }}
            </AutoSizer>
          </SortableContext>
        </DndContext>
      )}
    </div>
  );
}
