import type { DragEndEvent } from "@dnd-kit/core";
import type { TFunction } from "i18next";

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
import { AiOutlineArrowDown, AiOutlineArrowUp, AiOutlineHolder } from "react-icons/ai";

import { Button } from "@/components/bakaui";
import { PropertyValueScope, PropertyValueScopeLabel } from "@/sdk/constants";

export function scopeLabel(scope: PropertyValueScope, t: TFunction): string {
  const label = PropertyValueScopeLabel[scope];

  return label
    ? t<string>(`PropertyValueScope.${label}`)
    : t<string>("resourceProfile.scopePriority.unknownScope", { id: scope });
}

type Props = {
  scopes: PropertyValueScope[];
  onChange: (scopes: PropertyValueScope[]) => void;
  isDisabled?: boolean;
};

function ScopeRow({
  scope,
  index,
  count,
  onMove,
  isDisabled,
}: {
  scope: PropertyValueScope;
  index: number;
  count: number;
  onMove: (offset: number) => void;
  isDisabled?: boolean;
}) {
  const { t } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: scope,
    disabled: isDisabled,
  });
  const label = scopeLabel(scope, t);

  return (
    <li
      ref={setNodeRef}
      className="flex items-center gap-2 rounded-lg bg-default-100/60 px-2 py-1.5"
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
      }}
    >
      <button
        {...attributes}
        {...listeners}
        aria-label={t<string>("resourceProfile.scopePriority.reorder", { name: label })}
        className="touch-none rounded p-1 text-default-400 hover:text-foreground focus-visible:outline-primary"
        disabled={isDisabled}
        type="button"
      >
        <AiOutlineHolder className="text-lg" />
      </button>
      <span className="w-5 shrink-0 text-xs tabular-nums text-default-400">{index + 1}</span>
      <span
        className={`min-w-0 flex-1 text-sm ${scope === PropertyValueScope.Manual ? "text-primary" : ""}`}
      >
        {label}
      </span>
      <Button
        isIconOnly
        aria-label={t<string>("resourceProfile.scopePriority.moveUp", { name: label })}
        className="h-7 min-w-7 w-7"
        isDisabled={isDisabled || index === 0}
        size="sm"
        variant="light"
        onPress={() => onMove(-1)}
      >
        <AiOutlineArrowUp />
      </Button>
      <Button
        isIconOnly
        aria-label={t<string>("resourceProfile.scopePriority.moveDown", { name: label })}
        className="h-7 min-w-7 w-7"
        isDisabled={isDisabled || index === count - 1}
        size="sm"
        variant="light"
        onPress={() => onMove(1)}
      >
        <AiOutlineArrowDown />
      </Button>
    </li>
  );
}

export default function ScopePriorityList({ scopes, onChange, isDisabled }: Props) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  const move = (from: number, to: number) => {
    if (!isDisabled && from >= 0 && to >= 0 && to < scopes.length)
      onChange(arrayMove(scopes, from, to));
  };
  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (over && active.id !== over.id)
      move(
        scopes.indexOf(active.id as PropertyValueScope),
        scopes.indexOf(over.id as PropertyValueScope),
      );
  };

  return (
    <DndContext collisionDetection={closestCenter} sensors={sensors} onDragEnd={onDragEnd}>
      <SortableContext items={scopes} strategy={verticalListSortingStrategy}>
        <ol className="m-0 flex list-none flex-col gap-1 p-0">
          {scopes.map((scope, index) => (
            <ScopeRow
              key={scope}
              count={scopes.length}
              index={index}
              isDisabled={isDisabled}
              scope={scope}
              onMove={(offset) => move(index, index + offset)}
            />
          ))}
        </ol>
      </SortableContext>
    </DndContext>
  );
}
