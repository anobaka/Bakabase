"use client";

import type { CSSProperties } from "react";
import type { Tag } from "@/components/Property/models";

import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useTranslation } from "react-i18next";

import {
  ReferenceColor,
  ReferenceDragHandle,
  ReferenceItemActions,
} from "../../../ChoiceList/ReferenceItemTools";
import { tagText } from "../../../ChoiceList/helpers";

import { Input } from "@/components/bakaui";

interface Props {
  id: string;
  tag: Tag;
  compact?: boolean;
  onRemove?: (tag: Tag) => void;
  onChange?: (tag: Tag) => void;
  style?: CSSProperties;
  checkUsage?: (value: string) => Promise<number>;
}

export function SortableTag({ id, tag, compact, onRemove, onChange, style, checkUsage }: Props) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id,
  });

  return (
    <div
      ref={setNodeRef}
      className="pb-1"
      style={{
        ...style,
        transform: CSS.Transform.toString(transform),
        transition,
        zIndex: isDragging ? 1 : undefined,
      }}
    >
      <div
        className={`flex h-full items-center gap-0.5 rounded-lg bg-default-50 px-1 py-0.5 ${isDragging ? "shadow-md" : ""}`}
      >
        <ReferenceDragHandle
          {...listeners}
          {...attributes}
          aria-label={t("property.referenceEditor.drag")}
          title={t("property.referenceEditor.drag")}
        />
        <div className={`flex min-w-0 flex-1 gap-0.5 ${compact ? "flex-col" : "items-center"}`}>
          <div className="flex min-w-0 flex-1 items-center gap-0.5">
            <ReferenceColor color={tag.color} onChange={(color) => onChange?.({ ...tag, color })} />
            <div className="grid min-w-0 flex-1 grid-cols-2 gap-1">
              <Input
                aria-label={t("property.referenceEditor.tags.group")}
                className="min-w-0"
                classNames={{
                  inputWrapper: "h-8 min-h-8 rounded-md bg-default-100 px-2 shadow-none",
                }}
                placeholder={t("property.referenceEditor.tags.group")}
                size="sm"
                value={tag.group ?? ""}
                onValueChange={(group) => onChange?.({ ...tag, group })}
              />
              <Input
                aria-label={t("property.referenceEditor.tags.name")}
                className="min-w-0"
                classNames={{
                  inputWrapper: "h-8 min-h-8 rounded-md bg-default-100 px-2 shadow-none",
                }}
                placeholder={t("property.referenceEditor.tags.name")}
                size="sm"
                value={tag.name ?? ""}
                onValueChange={(name) => onChange?.({ ...tag, name })}
              />
            </div>
          </div>
          <div className="flex shrink-0 justify-end">
            <ReferenceItemActions
              checkUsage={checkUsage}
              hidden={tag.hide}
              label={tagText(tag)}
              value={tag.value}
              onRemove={() => onRemove?.(tag)}
              onToggleHidden={() => onChange?.({ ...tag, hide: !tag.hide })}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
