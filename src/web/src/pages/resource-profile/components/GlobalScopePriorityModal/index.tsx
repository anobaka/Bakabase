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

import type { DestroyableProps } from "@/components/bakaui/types";
import { Chip, Modal } from "@/components/bakaui";
import {
  PropertyValueScope,
  PropertyValueScopeLabel,
  propertyValueScopes,
} from "@/sdk/constants";
import { useResourceOptionsStore } from "@/stores/options";
import DragHandle from "@/components/DragHandle";

const SortableScopeItem = ({ scope, index }: { scope: PropertyValueScope; index: number }) => {
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
      className="flex items-center gap-2 py-2 px-3 border-b border-default-200 last:border-b-0 hover:bg-default-50"
      style={style}
    >
      <DragHandle {...listeners} {...attributes} />
      <span className="text-sm text-default-400 w-6">{index + 1}</span>
      <Chip
        size="sm"
        variant="flat"
        color={scope === PropertyValueScope.Manual ? "primary" : "default"}
      >
        {t(`PropertyValueScope.${PropertyValueScopeLabel[scope]}`)}
      </Chip>
    </div>
  );
};

type Props = DestroyableProps;

const GlobalScopePriorityModal = ({ onDestroyed }: Props) => {
  const { t } = useTranslation();
  const resourceOptionsStore = useResourceOptionsStore();
  const allScopes = useMemo(() => propertyValueScopes.map((s) => s.value), []);

  const [scopes, setScopes] = useState<PropertyValueScope[]>(() => {
    const saved = resourceOptionsStore.data?.propertyValueScopePriority;
    if (saved && saved.length > 0) {
      // Ensure all scopes are included (append any missing ones)
      const result = [...saved];
      for (const s of allScopes) {
        if (!result.includes(s)) {
          result.push(s);
        }
      }
      return result;
    }
    return allScopes;
  });

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const save = useCallback(
    (newScopes: PropertyValueScope[]) => {
      resourceOptionsStore.patch({ propertyValueScopePriority: newScopes });
    },
    [resourceOptionsStore]
  );

  const handleDragEnd = useCallback(
    (event: any) => {
      const { active, over } = event;
      if (active.id !== over?.id) {
        const oldIndex = scopes.indexOf(active.id as PropertyValueScope);
        const newIndex = scopes.indexOf(over?.id as PropertyValueScope);
        if (oldIndex !== -1 && newIndex !== -1) {
          const newScopes = arrayMove(scopes, oldIndex, newIndex);
          setScopes(newScopes);
          save(newScopes);
        }
      }
    },
    [scopes, save]
  );

  return (
    <Modal
      defaultVisible
      size="sm"
      title={t<string>("resourceProfile.globalScopePriority.title")}
      footer={{ actions: ["cancel"], cancelProps: { text: t<string>("common.action.close") } }}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-3">
        <div className="text-sm text-default-500">
          {t("resourceProfile.globalScopePriority.description")}
        </div>
        <DndContext
          collisionDetection={closestCenter}
          sensors={sensors}
          onDragEnd={handleDragEnd}
        >
          <SortableContext
            items={scopes}
            strategy={verticalListSortingStrategy}
          >
            <div className="border border-default-200 rounded-lg">
              {scopes.map((scope, index) => (
                <SortableScopeItem key={scope} scope={scope} index={index} />
              ))}
            </div>
          </SortableContext>
        </DndContext>
      </div>
    </Modal>
  );
};

GlobalScopePriorityModal.displayName = "GlobalScopePriorityModal";

export default GlobalScopePriorityModal;
