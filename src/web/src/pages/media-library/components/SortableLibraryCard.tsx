import type { ComponentProps, ReactNode } from "react";

import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { TbGripVertical } from "react-icons/tb";

type Props = Omit<ComponentProps<"article">, "children" | "id"> & {
  id: string;
  dragLabel: string;
  domId: string;
  disabled: boolean;
  children: (handle: ReactNode) => ReactNode;
};

export default function SortableLibraryCard({
  id,
  domId,
  disabled,
  dragLabel,
  children,
  style,
  ...props
}: Props) {
  const {
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id, disabled });

  return (
    <article
      {...props}
      ref={setNodeRef}
      id={domId}
      style={{
        ...style,
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
        position: "relative",
        zIndex: isDragging ? 10 : undefined,
      }}
    >
      {children(
        <button
          ref={setActivatorNodeRef}
          {...attributes}
          {...listeners}
          aria-label={dragLabel}
          className="flex h-8 w-6 shrink-0 touch-none cursor-grab items-center justify-center rounded text-default-400 hover:bg-default-200 focus-visible:outline-primary disabled:opacity-40"
          disabled={disabled}
          type="button"
        >
          <TbGripVertical aria-hidden size={18} />
        </button>,
      )}
    </article>
  );
}
