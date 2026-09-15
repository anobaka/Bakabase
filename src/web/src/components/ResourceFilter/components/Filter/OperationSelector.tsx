"use client";

import type { PropertyType, SearchOperation } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { AiOutlineDown } from "react-icons/ai";

import { getOperationDisplay, getOperationDropdownDisplay } from "./utils";

import {
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Tooltip,
  Button,
} from "@/components/bakaui";

export interface OperationSelectorProps {
  operation?: SearchOperation;
  propertyType?: PropertyType;
  /** Available operations for dropdown */
  availableOperations?: SearchOperation[];
  /** If true, property has been selected */
  hasProperty?: boolean;
  /** If true, renders as non-clickable Chip (no dropdown) */
  isReadonly?: boolean;
  /** Called when operation is selected */
  onSelect?: (operation: SearchOperation) => void;
}

/**
 * Displays and selects filter operation.
 * - Readonly mode: non-clickable Chip
 * - No property selected: disabled Button with tooltip
 * - No available operations: disabled Button with tooltip
 * - Normal mode: Dropdown with available operations
 */
const OperationSelector = ({
  operation,
  propertyType,
  availableOperations = [],
  hasProperty,
  isReadonly,
  onSelect,
}: OperationSelectorProps) => {
  const { t } = useTranslation();

  const displayText =
    operation === undefined
      ? t<string>("resourceFilter.condition.selectOperation")
      : getOperationDisplay(operation, propertyType, t);

  // Readonly mode: plain text
  if (isReadonly) {
    return (
      <span className="inline-flex min-h-8 items-center py-1 text-sm text-default-500">
        {displayText}
      </span>
    );
  }

  // No property selected: disabled text with tooltip
  if (!hasProperty) {
    return (
      <Tooltip content={t<string>("resourceFilter.condition.propertyRequired")}>
        <span className="inline-flex min-h-8 items-center py-1 text-sm text-default-400">
          {displayText}
        </span>
      </Tooltip>
    );
  }

  // No available operations: disabled text with tooltip
  if (!availableOperations || availableOperations.length === 0) {
    return (
      <Tooltip content={t<string>("resourceFilter.condition.noOperations")}>
        <span className="inline-flex min-h-8 items-center py-1 text-sm text-default-400">
          {displayText}
        </span>
      </Tooltip>
    );
  }

  // Normal mode: dropdown
  return (
    <Dropdown placement="bottom-start">
      <DropdownTrigger>
        <Button
          aria-label={t<string>("resourceFilter.condition.changeOperation", {
            operation: displayText,
          })}
          className="h-auto min-h-8 min-w-0 gap-1 px-2 py-1 text-sm text-default-600 whitespace-normal"
          endContent={<AiOutlineDown aria-hidden className="shrink-0 text-xs text-default-400" />}
          size="sm"
          variant="light"
        >
          {displayText}
        </Button>
      </DropdownTrigger>
      <DropdownMenu
        aria-label={t<string>("resourceFilter.condition.selectOperation")}
        selectedKeys={new Set(operation === undefined ? [] : [String(operation)])}
        selectionMode="single"
        onAction={(key) => onSelect?.(Number(key) as SearchOperation)}
      >
        {availableOperations.map((op) => {
          const { displayText: itemText, description } = getOperationDropdownDisplay(
            op,
            propertyType,
            t,
          );

          return (
            <DropdownItem key={op} description={description} textValue={itemText}>
              {itemText}
            </DropdownItem>
          );
        })}
      </DropdownMenu>
    </Dropdown>
  );
};

OperationSelector.displayName = "OperationSelector";

export default OperationSelector;
