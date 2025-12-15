"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathRule, BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React, { useCallback } from "react";
import { useTranslation } from "react-i18next";
import { SettingOutlined } from "@ant-design/icons";
import { AiOutlineAim } from "react-icons/ai";

import { Chip, Button, Tooltip } from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";
import MarkConfigModal from "./MarkConfigPopover";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  entry: Entry;
  pathRule?: BakabaseAbstractionsModelsDomainPathRule;
  onSaveMark?: (entry: Entry, pathRule: BakabaseAbstractionsModelsDomainPathRule | undefined, mark: BakabaseAbstractionsModelsDomainPathMark, oldMark?: BakabaseAbstractionsModelsDomainPathMark) => void;
  onDeleteMark?: (pathRule: BakabaseAbstractionsModelsDomainPathRule, mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onOpenAdvancedConfig?: (entry: Entry) => void;
};

/**
 * Parse and render a human-readable description of a path mark
 */
const getMarkDescription = (mark: BakabaseAbstractionsModelsDomainPathMark, t: (key: string, params?: any) => string): string => {
  try {
    const config = JSON.parse(mark.configJson || "{}");
    const isResource = mark.type === PathMarkType.Resource;
    const matchMode = config.MatchMode || config.matchMode;

    let description = "";

    if (matchMode === "Layer" || matchMode === 1) {
      const layer = config.Layer ?? config.layer ?? 0;
      if (layer === 0) {
        description = isResource
          ? t("Current item is resource")
          : t("Current item is property");
      } else {
        description = isResource
          ? t("{{layer}} level(s) down is resource", { layer })
          : t("{{layer}} level(s) down is property", { layer });
      }
    } else if (matchMode === "Regex" || matchMode === 2) {
      const regex = config.Regex ?? config.regex ?? "";
      description = isResource
        ? t("Regex '{{regex}}' matches resource", { regex })
        : t("Regex '{{regex}}' matches property", { regex });
    }

    // For property marks, add value information
    if (!isResource) {
      const valueType = config.ValueType ?? config.valueType;
      if (valueType === "Fixed" || valueType === 1) {
        const fixedValue = config.FixedValue ?? config.fixedValue;
        if (fixedValue) {
          description += ` = "${fixedValue}"`;
        }
      } else if (valueType === "Dynamic" || valueType === 2) {
        const valueLayer = config.ValueLayer ?? config.valueLayer;
        const valueRegex = config.ValueRegex ?? config.valueRegex;
        if (valueLayer !== undefined) {
          description += t(", value from {{layer}} level(s)", { layer: valueLayer });
        }
        if (valueRegex) {
          description += t(", regex: {{regex}}", { regex: valueRegex });
        }
      }

      // Add property ID if available
      const propertyId = config.PropertyId ?? config.propertyId;
      if (propertyId) {
        description += t(" (Property #{{id}})", { id: propertyId });
      }
    }

    // Add file type filter for resource marks
    if (isResource) {
      const fsTypeFilter = config.FsTypeFilter ?? config.fsTypeFilter;
      if (fsTypeFilter) {
        description += t(", type: {{type}}", { type: fsTypeFilter });
      }

      const extensions = config.Extensions ?? config.extensions;
      if (extensions && extensions.length > 0) {
        description += t(", ext: {{ext}}", { ext: extensions.join(", ") });
      }
    }

    return description;
  } catch (error) {
    console.error("Failed to parse mark config:", error);
    return t("Invalid mark configuration");
  }
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
      {/* Add Mark Buttons */}
      <div className="flex items-center gap-1">
        <Button
          size="sm"
          color="success"
          variant="light"
          startContent={<AiOutlineAim className="text-lg" />}
          onPress={() => handleAddMark(PathMarkType.Resource)}
        >
          {t("Mark as Resource")}
        </Button>

        <Button
          size="sm"
          color="primary"
          variant="light"
          startContent={<AiOutlineAim className="text-lg" />}
          onPress={() => handleAddMark(PathMarkType.Property)}
        >
          {t("Mark as Property")}
        </Button>

        {/* Advanced Config Button */}
        {onOpenAdvancedConfig && (
          <Button
            size="sm"
            color="default"
            variant="light"
            isIconOnly
            onPress={() => onOpenAdvancedConfig(entry)}
          >
            <SettingOutlined />
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
                <span className="font-medium">R{mark.priority}:</span>
                <span>{getMarkDescription(mark, t)}</span>
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
                <span className="font-medium">P{mark.priority}:</span>
                <span>{getMarkDescription(mark, t)}</span>
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
