"use client";

import { AiOutlineClose } from "react-icons/ai";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";

export interface DeleteButtonProps {
  onDelete?: () => void;
}

/**
 * Small delete button for filters.
 * Used in Simple mode where delete button is shown inline.
 */
const DeleteButton = ({ onDelete }: DeleteButtonProps) => {
  const { t } = useTranslation();
  const label = t<string>("resourceFilter.condition.remove");

  return (
    <Button
      isIconOnly
      aria-label={label}
      className="h-8 w-8 min-w-8 shrink-0 text-default-400 hover:text-danger"
      color="danger"
      size="sm"
      title={label}
      variant="light"
      onPress={onDelete}
    >
      <AiOutlineClose aria-hidden className="text-base" />
    </Button>
  );
};

DeleteButton.displayName = "DeleteButton";

export default DeleteButton;
