"use client";

import { AiOutlineClose } from "react-icons/ai";

import { Button } from "@/components/bakaui";

export interface DeleteButtonProps {
  onDelete?: () => void;
}

/**
 * Small delete button for filters.
 * Used in Simple mode where delete button is shown inline.
 */
const DeleteButton = ({ onDelete }: DeleteButtonProps) => {
  return (
    <Button
      isIconOnly
      size="sm"
      variant="light"
      color="danger"
      className="min-w-6 w-6 h-6"
      onPress={onDelete}
    >
      <AiOutlineClose className="text-sm" />
    </Button>
  );
};

DeleteButton.displayName = "DeleteButton";

export default DeleteButton;
