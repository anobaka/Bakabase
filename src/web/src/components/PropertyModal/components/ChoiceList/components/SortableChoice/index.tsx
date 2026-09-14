"use client";

import type { CSSProperties } from "react";
import type { IChoice } from "@/components/Property/models";

import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useTranslation } from "react-i18next";

import { ReferenceColor, ReferenceItemActions } from "../../ReferenceItemTools";

import DragHandle from "@/components/DragHandle";
import { Input } from "@/components/bakaui";

interface Props {
  id: string;
  choice: IChoice;
  compact?: boolean;
  onRemove?: (choice: IChoice) => void;
  onChange?: (choice: IChoice) => void;
  style?: CSSProperties;
  checkUsage?: (value: string) => Promise<number>;
  onEnterKeyDown?: () => void;
}

export function SortableChoice({
  id,
  choice,
  compact,
  onRemove,
  onChange,
  style,
  checkUsage,
  onEnterKeyDown,
}: Props) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id,
  });

  return (
    <div
      ref={setNodeRef}
      className="pb-1.5"
      style={{
        ...style,
        transform: CSS.Transform.toString(transform),
        transition,
        zIndex: isDragging ? 1 : undefined,
      }}
    >
      <div
        className={`flex h-full items-center gap-1.5 rounded-xl bg-default-50 px-2 py-2 ${isDragging ? "shadow-md" : ""}`}
      >
        <DragHandle
          {...listeners}
          {...attributes}
          aria-label={t("property.referenceEditor.drag")}
          className="shrink-0"
          title={t("property.referenceEditor.drag")}
        />
        <div className={`flex min-w-0 flex-1 gap-1.5 ${compact ? "flex-col" : "items-center"}`}>
          <div className="flex min-w-0 flex-1 items-center gap-1.5">
            <ReferenceColor
              color={choice.color}
              onChange={(color) => onChange?.({ ...choice, color })}
            />
            <Input
              aria-label={t("property.referenceEditor.choices.name")}
              className="min-w-0 flex-1"
              classNames={{ inputWrapper: "bg-default-100 shadow-none" }}
              placeholder={t("property.referenceEditor.choices.name")}
              size="sm"
              value={choice.label ?? ""}
              onKeyDown={(event) => {
                if (event.key === "Enter") onEnterKeyDown?.();
              }}
              onValueChange={(label) => onChange?.({ ...choice, label })}
            />
          </div>
          <div className="flex shrink-0 justify-end">
            <ReferenceItemActions
              checkUsage={checkUsage}
              hidden={choice.hide}
              label={choice.label}
              value={choice.value}
              onRemove={() => onRemove?.(choice)}
              onToggleHidden={() => onChange?.({ ...choice, hide: !choice.hide })}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
