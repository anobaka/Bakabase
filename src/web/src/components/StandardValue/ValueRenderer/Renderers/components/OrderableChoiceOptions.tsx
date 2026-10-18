import type { ReactNode } from "react";
import type { OptionDisplayProps } from "../../../OptionDisplayProps";
import type { DragEndEvent } from "@dnd-kit/core";

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
  rectSortingStrategy,
  sortableKeyboardCoordinates,
  useSortable,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { TbGripVertical } from "react-icons/tb";
import { useRef, useState, useSyncExternalStore } from "react";

type Choice = { value: string; label: string };
type Ordering = NonNullable<OptionDisplayProps["optionOrdering"]>;
const EmptyKeys: readonly string[] = [];
const noSubscribe = () => () => {};
const emptySnapshot = () => EmptyKeys;

function SortableChoice({
  choice,
  ordering,
  children,
}: {
  choice: Choice;
  ordering: Ordering;
  children: ReactNode;
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: choice.value, disabled: ordering.disabled });

  return (
    <div
      ref={setNodeRef}
      className="inline-flex max-w-full items-center gap-0.5 rounded-lg"
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
        position: "relative",
        zIndex: isDragging ? 10 : undefined,
      }}
    >
      <button
        ref={setActivatorNodeRef}
        {...attributes}
        {...listeners}
        aria-label={ordering.dragLabel(choice.label)}
        className="flex h-6 w-4 shrink-0 touch-none cursor-grab items-center justify-center rounded text-default-400 hover:bg-default-200 focus-visible:outline-primary disabled:opacity-40"
        disabled={ordering.disabled}
        type="button"
      >
        <TbGripVertical aria-hidden size={14} />
      </button>
      {children}
    </div>
  );
}

export default function OrderableChoiceOptions({
  choices,
  ordering,
  renderOption,
  children,
}: {
  choices: Choice[];
  ordering: Ordering;
  renderOption: (choice: Choice) => ReactNode;
  children?: ReactNode;
}) {
  const pending = useRef(false);
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);
  const orderedKeys = useSyncExternalStore(
    ordering.source?.subscribe ?? noSubscribe,
    ordering.source?.getSnapshot ?? emptySnapshot,
  );
  const positions = new Map(orderedKeys.map((key, index) => [key, index]));
  const visible = [...choices].sort(
    (a, b) =>
      (positions.get(a.value) ?? orderedKeys.length) -
      (positions.get(b.value) ?? orderedKeys.length),
  );
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  const onDragEnd = async ({ active, over }: DragEndEvent) => {
    if (pending.current || ordering.disabled || !over || active.id === over.id) return;
    pending.current = true;
    setSaving(true);
    setFailed(false);
    try {
      const success = await ordering.onMove(
        String(active.id),
        String(over.id),
        visible.map(({ value }) => value),
      );

      setFailed(!success);
    } finally {
      pending.current = false;
      setSaving(false);
    }
  };

  return (
    <div className="flex min-w-0 flex-col gap-1">
      {ordering.toolbar}
      {failed && (
        <span className="text-xs text-danger" role="alert">
          {ordering.failureLabel}
        </span>
      )}
      <DndContext collisionDetection={closestCenter} sensors={sensors} onDragEnd={onDragEnd}>
        <SortableContext items={visible.map(({ value }) => value)} strategy={rectSortingStrategy}>
          <div className="flex flex-wrap items-center gap-1">
            {visible.map((choice) => (
              <SortableChoice
                key={choice.value}
                choice={choice}
                ordering={{ ...ordering, disabled: ordering.disabled || saving }}
              >
                {renderOption(choice)}
              </SortableChoice>
            ))}
            {children}
          </div>
        </SortableContext>
      </DndContext>
    </div>
  );
}
