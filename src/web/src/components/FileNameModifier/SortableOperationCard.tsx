"use client";

import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "@/sdk/Api";

import React from "react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";

import OperationCard from "./OperationCard";

interface SortableOperationCardProps {
  id: string;
  operation: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
  index: number;
  errors?: string;
  onChange: (
    op: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation,
  ) => void;
  onDelete: () => void;
  onCopy?: () => void;
}

const SortableOperationCard: React.FC<SortableOperationCardProps> = ({
  id,
  operation,
  index,
  errors,
  onChange,
  onDelete,
  onCopy,
}) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
    zIndex: isDragging ? 1000 : undefined,
  };

  return (
    <OperationCard
      ref={setNodeRef}
      dragHandleProps={{ ...listeners, ...attributes }}
      errors={errors}
      index={index}
      operation={operation}
      style={style}
      onChange={onChange}
      onCopy={onCopy}
      onDelete={onDelete}
    />
  );
};

export default SortableOperationCard;
