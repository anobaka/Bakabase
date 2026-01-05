"use client";

import type { PropertyType, SearchOperation } from "@/sdk/constants";

import { useTranslation } from "react-i18next";

import { getOperationDisplay, getOperationDropdownDisplay } from "./utils";

import {
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Tooltip,
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

  const displayText = operation === undefined
    ? t<string>("Condition")
    : getOperationDisplay(operation, propertyType, t);

  // Readonly mode: plain text
  if (isReadonly) {
    return (
      <span className="text-sm text-secondary">
        {displayText}
      </span>
    );
  }

  // No property selected: disabled text with tooltip
  if (!hasProperty) {
    return (
      <Tooltip content={t<string>("Please select a property first")}>
        <span className="text-sm text-default-400 cursor-not-allowed">
          {displayText}
        </span>
      </Tooltip>
    );
  }

  // No available operations: disabled text with tooltip
  if (availableOperations.length === 0) {
    return (
      <Tooltip content={t<string>("Can not operate on this property")}>
        <span className="text-sm text-default-400 cursor-not-allowed">
          {displayText}
        </span>
      </Tooltip>
    );
  }

  // Normal mode: dropdown
  return (
    <Dropdown placement="bottom-start">
      <DropdownTrigger>
        <button
          type="button"
          className="text-sm text-secondary hover:text-secondary-600 hover:underline cursor-pointer bg-transparent border-none p-0"
        >
          {displayText}
        </button>
      </DropdownTrigger>
      <DropdownMenu>
        {availableOperations.map((op) => {
          const { displayText: itemText, description } = getOperationDropdownDisplay(
            op,
            propertyType,
            t
          );

          return (
            <DropdownItem
              key={op}
              description={description}
              onClick={() => onSelect?.(op)}
            >
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
