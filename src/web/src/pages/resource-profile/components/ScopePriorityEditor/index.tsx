"use client";

import { useCallback, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
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

import { Button, Chip, Tooltip } from "@/components/bakaui";
import { PropertyValueScope, PropertyValueScopeLabel } from "@/sdk/constants";
import DragHandle from "@/components/DragHandle";
import { DeleteOutlined, UndoOutlined } from "@ant-design/icons";

type Props = {
  /** Current scope priority. null means "use global setting". */
  value: PropertyValueScope[] | null;
  /** Available scopes to show in the editor */
  availableScopes: PropertyValueScope[];
  onChange: (value: PropertyValueScope[] | null) => void;
};

const SortableScopeItem = ({
  scope,
  index,
}: {
  scope: PropertyValueScope;
  index: number;
}) => {
  const { t } = useTranslation();
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: scope });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  return (
    <div
      ref={setNodeRef}
      className="flex items-center gap-2 py-1.5 px-2 border-b border-default-200 last:border-b-0 hover:bg-default-50"
      style={style}
    >
      <DragHandle {...listeners} {...attributes} />
      <span className="text-xs text-default-400 w-5">{index + 1}</span>
      <Chip
        size="sm"
        variant="flat"
        color={scope === PropertyValueScope.Manual ? "primary" : "default"}
      >
        {t(PropertyValueScopeLabel[scope])}
      </Chip>
    </div>
  );
};

const ScopePriorityEditor = ({ value, availableScopes, onChange }: Props) => {
  const { t } = useTranslation();
  const isCustom = value !== null;

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const currentScopes = useMemo(() => {
    if (value !== null) return value;
    return availableScopes;
  }, [value, availableScopes]);

  const handleDragEnd = useCallback(
    (event: any) => {
      const { active, over } = event;
      if (active.id !== over?.id) {
        const oldIndex = currentScopes.indexOf(active.id as PropertyValueScope);
        const newIndex = currentScopes.indexOf(over?.id as PropertyValueScope);
        if (oldIndex !== -1 && newIndex !== -1) {
          const newScopes = arrayMove(currentScopes, oldIndex, newIndex);
          onChange(newScopes);
        }
      }
    },
    [currentScopes, onChange]
  );

  const handleCustomize = () => {
    onChange([...availableScopes]);
  };

  const handleReset = () => {
    onChange(null);
  };

  if (!isCustom) {
    return (
      <div className="flex items-center gap-2">
        <span className="text-sm text-default-500">
          {t("resourceProfile.scopePriority.useGlobal")}
        </span>
        <Tooltip content={t("resourceProfile.scopePriority.customizeTooltip")}>
          <Button
            size="sm"
            variant="flat"
            onPress={handleCustomize}
          >
            {t("resourceProfile.scopePriority.customize")}
          </Button>
        </Tooltip>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <span className="text-xs text-default-400">
          {t("resourceProfile.scopePriority.customLabel")}
        </span>
        <Tooltip content={t("resourceProfile.scopePriority.resetTooltip")}>
          <Button
            isIconOnly
            size="sm"
            variant="light"
            onPress={handleReset}
          >
            <UndoOutlined className="text-xs" />
          </Button>
        </Tooltip>
      </div>
      <DndContext
        collisionDetection={closestCenter}
        sensors={sensors}
        onDragEnd={handleDragEnd}
      >
        <SortableContext
          items={currentScopes}
          strategy={verticalListSortingStrategy}
        >
          <div className="border border-default-200 rounded-md">
            {currentScopes.map((scope, index) => (
              <SortableScopeItem
                key={scope}
                scope={scope}
                index={index}
              />
            ))}
          </div>
        </SortableContext>
      </DndContext>
    </div>
  );
};

ScopePriorityEditor.displayName = "ScopePriorityEditor";

export default ScopePriorityEditor;
