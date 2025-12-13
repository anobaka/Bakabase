"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useState, useMemo, useCallback } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineFolder,
  AiOutlineFolderOpen,
  AiOutlineRight,
  AiOutlineDown,
  AiOutlineWarning,
  AiOutlineDelete,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineCopy,
  AiOutlineSnippets,
} from "react-icons/ai";

import { Button, toast, Tooltip, Dropdown, DropdownTrigger, DropdownMenu, DropdownItem } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import MarkConfigModal from "@/pages/path-mark-config/components/MarkConfigModal";
import PathMarkChip from "@/pages/path-mark-config/components/PathMarkChip";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useCopyMarksStore } from "@/stores/copyMarks";
import { getNewMarks, getExistingMarksCount } from "@/pages/path-mark-config/utils/markComparison";
import { PathMarkType } from "@/sdk/constants";
import { AiOutlinePlus } from "react-icons/ai";

export interface PathTreeNode {
  name: string;
  fullPath: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  children: Map<string, PathTreeNode>;
  exists?: boolean;
}

export interface TreeNodeComponentProps {
  node: PathTreeNode;
  depth: number;
  onSaveMark: (
    path: string,
    mark: BakabaseAbstractionsModelsDomainPathMark,
    oldMark?: BakabaseAbstractionsModelsDomainPathMark,
  ) => void;
  onDeleteMark: (path: string, mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onConfigureSubPathMarks: (path: string) => void;
  onDeletePathMarks: (path: string) => void;
  onPasteMarks: (path: string, marks: BakabaseAbstractionsModelsDomainPathMark[]) => void;
}

const TreeNodeComponent = ({
  node,
  depth,
  onSaveMark,
  onDeleteMark,
  onConfigureSubPathMarks,
  onDeletePathMarks,
  onPasteMarks,
}: TreeNodeComponentProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [expanded, setExpanded] = useState(true);

  // Copy/paste store
  const {
    candidateGroups,
    selectedGroupId,
    copyModeEntryPath,
    selectedMarkIds,
    enterCopyMode,
    exitCopyMode,
    toggleMarkSelection,
    selectAllMarks,
    confirmSelection,
  } = useCopyMarksStore();

  const isInCopyMode = copyModeEntryPath === node.fullPath;

  const hasChildren = node.children.size > 0;
  const hasMarks = node.marks.length > 0;
  const children = Array.from(node.children.values());

  // Calculate paste info
  const pasteInfo = useMemo(() => {
    const selectedGroup = candidateGroups.find((g) => g.id === selectedGroupId);
    if (!selectedGroup || selectedGroup.sourcePath === node.fullPath) {
      return { canPaste: false, newMarks: [], existingCount: 0 };
    }
    const newMarks = getNewMarks(selectedGroup.marks, node.marks);
    const existingCount = getExistingMarksCount(selectedGroup.marks, node.marks);
    return { canPaste: newMarks.length > 0, newMarks, existingCount };
  }, [candidateGroups, selectedGroupId, node.fullPath, node.marks]);

  const handleCopyPath = useCallback(() => {
    navigator.clipboard.writeText(node.fullPath);
    toast.success(t("Path copied to clipboard"));
  }, [node.fullPath, t]);

  const handleConfigureSubPathMarks = useCallback(() => {
    onConfigureSubPathMarks(node.fullPath);
  }, [node.fullPath, onConfigureSubPathMarks]);

  const handleDeletePathMarks = useCallback(() => {
    onDeletePathMarks(node.fullPath);
  }, [node.fullPath, onDeletePathMarks]);

  // Enter copy mode and select all marks by default
  const handleEnterCopyMode = useCallback(() => {
    if (node.marks.length > 0) {
      enterCopyMode(node.fullPath);
      const markIds = node.marks.filter((m) => m.id !== undefined).map((m) => m.id!);
      selectAllMarks(markIds);
    }
  }, [enterCopyMode, node.fullPath, node.marks, selectAllMarks]);

  // Confirm selected marks for copy
  const handleConfirmCopy = useCallback(() => {
    confirmSelection(node.fullPath, node.marks);
  }, [confirmSelection, node.fullPath, node.marks]);

  // Cancel copy mode
  const handleCancelCopy = useCallback(() => {
    exitCopyMode();
  }, [exitCopyMode]);

  const handlePasteMarks = useCallback(() => {
    if (pasteInfo.canPaste) {
      onPasteMarks(node.fullPath, pasteInfo.newMarks);
    }
  }, [node.fullPath, pasteInfo, onPasteMarks]);

  const [syncing, setSyncing] = useState(false);
  const handleSyncPath = useCallback(async () => {
    setSyncing(true);
    try {
      await BApi.pathMark.startPathMarkSyncByPath({ path: node.fullPath });
      toast.success(t("Sync started"));
    } catch (e) {
      toast.danger(t("Failed to start sync"));
    } finally {
      setSyncing(false);
    }
  }, [node.fullPath, t]);

  const handleEditMark = useCallback(
    (mark: BakabaseAbstractionsModelsDomainPathMark) => {
      createPortal(MarkConfigModal, {
        mark,
        markType: mark.type!,
        rootPath: node.fullPath,
        onSave: async (newMark) => {
          onSaveMark(node.fullPath, newMark, mark);
        },
      });
    },
    [createPortal, node.fullPath, onSaveMark],
  );

  const handleDeleteMark = useCallback(
    (mark: BakabaseAbstractionsModelsDomainPathMark) => {
      onDeleteMark(node.fullPath, mark);
    },
    [node.fullPath, onDeleteMark],
  );

  const handleAddMark = useCallback(
    (markType: PathMarkType) => {
      createPortal(MarkConfigModal, {
        markType,
        rootPath: node.fullPath,
        onSave: async (newMark) => {
          onSaveMark(node.fullPath, newMark);
        },
      });
    },
    [createPortal, node.fullPath, onSaveMark],
  );

  const isInvalid = node.exists === false;

  return (
    <div>
      {/* Node row */}
      <div
        className={`flex items-center py-1.5 px-2 hover:bg-default-100 rounded-md group ${isInvalid ? "bg-danger-50" : ""}`}
        style={{ paddingLeft: `${depth * 20 + 8}px` }}
      >
        {/* Expand/collapse toggle */}
        {hasChildren ? (
          <Button
            isIconOnly
            className="min-w-0 w-5 h-5 mr-1"
            size="sm"
            variant="light"
            onPress={() => setExpanded(!expanded)}
          >
            {expanded ? (
              <AiOutlineDown className="text-sm" />
            ) : (
              <AiOutlineRight className="text-sm" />
            )}
          </Button>
        ) : (
          <span className="w-6 mr-1" />
        )}

        {/* Folder icon */}
        {isInvalid ? (
          <AiOutlineWarning className="text-danger mr-2" />
        ) : hasChildren || hasMarks ? (
          expanded ? (
            <AiOutlineFolderOpen className="text-warning mr-2" />
          ) : (
            <AiOutlineFolder className="text-warning mr-2" />
          )
        ) : (
          <AiOutlineFolder className="text-default-400 mr-2" />
        )}

        {/* Path name - click to copy */}
        <Tooltip content={t("Click to copy full path")}>
          <span
            className={`font-mono text-sm cursor-pointer hover:text-primary ${isInvalid ? "text-danger line-through" : ""}`}
            onClick={handleCopyPath}
          >
            {node.name}
          </span>
        </Tooltip>

        {/* Invalid indicator */}
        {isInvalid && (
          <span className="text-xs text-danger ml-2">({t("Invalid")})</span>
        )}

        {/* Add mark and copy buttons - right after path name */}
        {!isInCopyMode && !isInvalid && (
          <div className="flex items-center gap-0.5 ml-2">
            {/* Add mark dropdown */}
            <Dropdown>
              <DropdownTrigger>
                <Button
                  isIconOnly
                  className="min-w-0 w-5 h-5"
                  color="success"
                  size="sm"
                  variant="light"
                >
                  <AiOutlinePlus className="text-base" />
                </Button>
              </DropdownTrigger>
              <DropdownMenu
                aria-label="Add mark options"
                onAction={(key) => handleAddMark(Number(key) as PathMarkType)}
              >
                <DropdownItem key={PathMarkType.Resource} className="text-success">
                  {t("Resource Mark")}
                </DropdownItem>
                <DropdownItem key={PathMarkType.Property} className="text-primary">
                  {t("Property Mark")}
                </DropdownItem>
                <DropdownItem key={PathMarkType.MediaLibrary} className="text-warning">
                  {t("MediaLibrary Mark")}
                </DropdownItem>
              </DropdownMenu>
            </Dropdown>

            {/* Copy marks button - only for paths with marks */}
            {hasMarks && (
              <Tooltip content={t("Copy marks from this path")}>
                <Button
                  isIconOnly
                  className="min-w-0 w-5 h-5"
                  color="default"
                  size="sm"
                  variant="light"
                  onPress={handleEnterCopyMode}
                >
                  <AiOutlineCopy className="text-base" />
                </Button>
              </Tooltip>
            )}
          </div>
        )}

        {/* Marks */}
        {hasMarks && (
          <div className="flex items-center gap-1 ml-3 flex-wrap">
            {node.marks.map((mark) => (
              <PathMarkChip
                key={`mark-${mark.id}`}
                mark={mark}
                selectable={isInCopyMode}
                selected={mark.id !== undefined && selectedMarkIds.includes(mark.id)}
                onClick={isInCopyMode ? undefined : () => handleEditMark(mark)}
                onContextMenu={isInCopyMode ? undefined : () => handleDeleteMark(mark)}
                onSelectionChange={() => {
                  if (mark.id !== undefined) {
                    toggleMarkSelection(mark.id);
                  }
                }}
              />
            ))}
          </div>
        )}

        {/* Copy mode actions */}
        {isInCopyMode && (
          <div className="flex items-center gap-1 ml-3">
            <Button
              color="primary"
              size="sm"
              isDisabled={selectedMarkIds.length === 0}
              onPress={handleConfirmCopy}
            >
              {t("Confirm selection")} ({selectedMarkIds.length}/{node.marks.length})
            </Button>
            <Button
              size="sm"
              variant="light"
              onPress={handleCancelCopy}
            >
              {t("Cancel")}
            </Button>
          </div>
        )}

        {/* Action buttons - always visible on hover, hidden in copy mode */}
        {!isInCopyMode && (
          <div className="flex items-center gap-1 ml-auto opacity-0 group-hover:opacity-100 transition-opacity">
            {/* Configure sub-path marks button - only for valid paths */}
            {!isInvalid && (
              <Tooltip content={t("Configure sub-path marks")}>
                <Button
                  isIconOnly
                  className="min-w-0 w-6 h-6"
                  color="primary"
                  size="sm"
                  variant="light"
                  onPress={handleConfigureSubPathMarks}
                >
                  <AiOutlineSetting className="text-sm" />
                </Button>
              </Tooltip>
            )}

            {/* Sync button - only for paths with marks */}
            {hasMarks && (
              <Tooltip content={t("Sync all marks on this path")}>
                <Button
                  isIconOnly
                  className="min-w-0 w-6 h-6"
                  color="success"
                  size="sm"
                  variant="light"
                  isLoading={syncing}
                  onPress={handleSyncPath}
                >
                  <AiOutlineSync className="text-sm" />
                </Button>
              </Tooltip>
            )}

            {/* Paste button - only when marks can be pasted */}
            {pasteInfo.canPaste && (
              <Tooltip
                content={
                  pasteInfo.existingCount > 0
                    ? t("Paste {{count}} marks ({{existing}} already exist)", {
                        count: pasteInfo.newMarks.length,
                        existing: pasteInfo.existingCount,
                      })
                    : t("Paste {{count}} marks", { count: pasteInfo.newMarks.length })
                }
              >
                <Button
                  isIconOnly
                  className="min-w-0 w-6 h-6"
                  color="warning"
                  size="sm"
                  variant="flat"
                  onPress={handlePasteMarks}
                >
                  <AiOutlineSnippets className="text-sm" />
                </Button>
              </Tooltip>
            )}

            {/* Delete button - for all paths */}
            <Tooltip content={t("Delete all marks on this path")}>
              <Button
                isIconOnly
                className="min-w-0 w-6 h-6"
                color="danger"
                size="sm"
                variant="light"
                onPress={handleDeletePathMarks}
              >
                <AiOutlineDelete className="text-sm" />
              </Button>
            </Tooltip>
          </div>
        )}
      </div>

      {/* Children */}
      {expanded && hasChildren && (
        <div>
          {children
            .sort((a, b) => a.name.localeCompare(b.name))
            .map((child) => (
              <TreeNodeComponent
                key={child.fullPath}
                depth={depth + 1}
                node={child}
                onConfigureSubPathMarks={onConfigureSubPathMarks}
                onDeleteMark={onDeleteMark}
                onDeletePathMarks={onDeletePathMarks}
                onPasteMarks={onPasteMarks}
                onSaveMark={onSaveMark}
              />
            ))}
        </div>
      )}
    </div>
  );
};

TreeNodeComponent.displayName = "TreeNodeComponent";

export default TreeNodeComponent;
