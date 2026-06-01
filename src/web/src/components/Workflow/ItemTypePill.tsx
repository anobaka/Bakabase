import type { WorkflowItemTypeIndex } from "./itemTypeRegistry";

import React from "react";
import { useTranslation } from "react-i18next";
import { Popover, PopoverContent, PopoverTrigger } from "@heroui/react";
import { AiOutlineQuestion } from "react-icons/ai";

import { Chip } from "@/components/bakaui";

interface Props {
  itemType: string;
  index: WorkflowItemTypeIndex;
  /** Display "→ Source: name" when this pill represents the trigger's output. */
  fromTrigger?: boolean;
  /** Red border + dot when the activity at this position can't accept the type. */
  invalid?: boolean;
}

/**
 * Tiny chip rendered between cards (and below the trigger). Shows the item type's display
 * name; click to reveal a popover with the reflected field list — so users can see exactly
 * what shape is flowing through the chain at any point.
 */
const ItemTypePill: React.FC<Props> = ({ itemType, index, fromTrigger, invalid }) => {
  const { t } = useTranslation();
  const descriptor = index.get(itemType);
  // The server ships an English display name; the editor prefers a localized version keyed
  // by `itemType`, falling back to the server's value and finally the raw tag.
  const label = t<string>(`workflow.itemType.${itemType}.displayName`, {
    defaultValue: descriptor?.displayName ?? itemType,
  });

  return (
    <div className="flex items-center gap-1 my-1 ml-2">
      <span className="text-default-400 text-xs">
        {fromTrigger ? t<string>("workflow.itemType.from.trigger") : "↓"}
      </span>
      <Popover placement="right">
        <PopoverTrigger>
          <Chip
            className="cursor-pointer"
            color={invalid ? "danger" : "default"}
            size="sm"
            startContent={<AiOutlineQuestion className="text-xs" />}
            variant="flat"
          >
            <span className="font-mono text-xs">{label}</span>
          </Chip>
        </PopoverTrigger>
        <PopoverContent>
          <div className="p-2 flex flex-col gap-1 max-w-xs">
            <div className="text-sm font-semibold">{label}</div>
            <div className="text-xs text-default-400 font-mono">{itemType}</div>
            {descriptor?.fields && descriptor.fields.length > 0 ? (
              <div className="mt-1 flex flex-col gap-0.5 font-mono text-xs">
                {descriptor.fields.map((f) => (
                  <div key={f.name} className="flex items-baseline gap-2">
                    <span>{f.name}</span>
                    <span className="text-default-400">
                      {f.type}
                      {f.nullable ? "?" : ""}
                    </span>
                  </div>
                ))}
              </div>
            ) : (
              <div className="text-xs text-default-500 italic">
                {t<string>("workflow.itemType.noFields")}
              </div>
            )}
          </div>
        </PopoverContent>
      </Popover>
    </div>
  );
};

export default ItemTypePill;
