"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathRule, BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React, { useCallback } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineAim, AiOutlineEye } from "react-icons/ai";

import { Chip, Button, Tooltip, Dropdown, DropdownTrigger, DropdownMenu, DropdownItem } from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";
import MarkConfigModal from "./MarkConfigPopover";
import MarkDescription from "./MarkDescription";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  entry: Entry;
  pathRule?: BakabaseAbstractionsModelsDomainPathRule;
  onSaveMark?: (entry: Entry, pathRule: BakabaseAbstractionsModelsDomainPathRule | undefined, mark: BakabaseAbstractionsModelsDomainPathMark, oldMark?: BakabaseAbstractionsModelsDomainPathMark) => void;
  onDeleteMark?: (pathRule: BakabaseAbstractionsModelsDomainPathRule, mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onOpenAdvancedConfig?: (entry: Entry) => void;
};

const PathRuleMarks = ({ entry, pathRule, onSaveMark, onDeleteMark, onOpenAdvancedConfig }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const marks = pathRule?.marks || [];
  const resourceMarks = marks.filter(m => m.type === PathMarkType.Resource);
  const propertyMarks = marks.filter(m => m.type === PathMarkType.Property);

  const handleAddMark = useCallback((markType: PathMarkType) => {
    createPortal(MarkConfigModal, {
      markType,
      rootPath: entry.path,
      onSave: async (mark) => {
        if (onSaveMark) {
          onSaveMark(entry, pathRule, mark);
        }
      },
    });
  }, [createPortal, entry, pathRule, onSaveMark]);

  const handleEditMark = useCallback((mark: BakabaseAbstractionsModelsDomainPathMark) => {
    createPortal(MarkConfigModal, {
      mark,
      markType: mark.type!,
      rootPath: entry.path,
      onSave: async (newMark) => {
        if (onSaveMark) {
          onSaveMark(entry, pathRule, newMark, mark);
        }
      },
    });
  }, [createPortal, entry, pathRule, onSaveMark]);

  const handleDeleteMark = useCallback((mark: BakabaseAbstractionsModelsDomainPathMark) => {
    if (pathRule && onDeleteMark) {
      onDeleteMark(pathRule, mark);
    }
  }, [pathRule, onDeleteMark]);

  return (
    <div
      className="flex items-center gap-2 ml-2"
      onClick={(e) => e.stopPropagation()}
      onMouseDown={(e) => e.stopPropagation()}
    >
      {/* Add Mark Dropdown */}
      <div className="flex items-center gap-1">
        <Dropdown>
          <DropdownTrigger>
            <Button
              size="sm"
              variant="light"
              className="min-w-0 px-2 text-default-400 hover:text-primary transition-colors"
              startContent={<AiOutlineAim className="text-lg" />}
            >
              {t("Add Mark")}
            </Button>
          </DropdownTrigger>
          <DropdownMenu
            aria-label="Mark type selection"
            onAction={(key) => handleAddMark(Number(key) as PathMarkType)}
          >
            <DropdownItem key={PathMarkType.Resource} className="text-success">
              {t("Resource")}
            </DropdownItem>
            <DropdownItem key={PathMarkType.Property} className="text-primary">
              {t("Property")}
            </DropdownItem>
          </DropdownMenu>
        </Dropdown>

        {/* All Marks Button - only show when there are marks */}
        {onOpenAdvancedConfig && marks.length > 0 && (
          <Button
            size="sm"
            color="default"
            variant="light"
            className="min-w-0 px-2 text-default-400 hover:text-primary transition-colors"
            onPress={() => onOpenAdvancedConfig(entry)}
          >
            <AiOutlineEye className="text-lg" />
            {t("All Marks")}
          </Button>
        )}
      </div>

      {/* Display Marks - Click to Edit */}
      <div className="flex items-center gap-1 flex-wrap">
        {resourceMarks.map((mark, idx) => (
          <Tooltip key={`resource-${idx}`} content={t("Click to edit, right-click to delete")}>
            <Chip
              size="sm"
              color="success"
              variant="flat"
              className="cursor-pointer hover:opacity-80"
              onClick={() => handleEditMark(mark)}
              onContextMenu={(e) => {
                e.preventDefault();
                handleDeleteMark(mark);
              }}
            >
              <div className="flex items-center gap-1 text-xs">
                <span className="font-medium">[{t("Resource")}#{mark.priority}]</span>
                <MarkDescription mark={mark} />
              </div>
            </Chip>
          </Tooltip>
        ))}

        {propertyMarks.map((mark, idx) => (
          <Tooltip key={`property-${idx}`} content={t("Click to edit, right-click to delete")}>
            <Chip
              size="sm"
              color="primary"
              variant="flat"
              className="cursor-pointer hover:opacity-80"
              onClick={() => handleEditMark(mark)}
              onContextMenu={(e) => {
                e.preventDefault();
                handleDeleteMark(mark);
              }}
            >
              <div className="flex items-center gap-1 text-xs">
                <span className="font-medium">[{t("Property")}#{mark.priority}]</span>
                <MarkDescription mark={mark} />
              </div>
            </Chip>
          </Tooltip>
        ))}
      </div>
    </div>
  );
};

PathRuleMarks.displayName = "PathRuleMarks";

export default PathRuleMarks;
