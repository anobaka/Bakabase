"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { PathMarkGroup } from "@/pages/path-mark-config/hooks/usePathMarks";

import { useState, useMemo, useCallback } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineFolder,
  AiOutlineFolderOpen,
  AiOutlineRight,
  AiOutlineDown,
  AiOutlineWarning,
  AiOutlineDelete,
  AiOutlineSwap,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineCopy,
  AiOutlineSnippets,
} from "react-icons/ai";

import { Button, toast, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import MarkConfigModal from "@/pages/path-mark-config/components/MarkConfigModal";
import PathMarkChip from "@/pages/path-mark-config/components/PathMarkChip";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useCopyMarksStore } from "@/stores/copyMarks";
import { getNewMarks, getExistingMarksCount } from "@/pages/path-mark-config/utils/markComparison";

interface PathTreeNode {
  name: string;
  fullPath: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  children: Map<string, PathTreeNode>;
  exists?: boolean;
}

interface PathTreeProps {
  groups: PathMarkGroup[];
  onSaveMark: (
    path: string,
    mark: BakabaseAbstractionsModelsDomainPathMark,
    oldMark?: BakabaseAbstractionsModelsDomainPathMark,
  ) => void;
  onDeleteMark: (path: string, mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onConfigurePath: (path: string) => void; // Open config modal for valid paths
  onTransferMarks: (fromPath: string) => void; // Transfer marks from path
  onDeletePathMarks: (path: string) => void; // Delete all marks on a path
  onPasteMarks: (path: string, marks: BakabaseAbstractionsModelsDomainPathMark[]) => void; // Paste marks to path
}

// Build a tree structure from path groups
function buildPathTree(groups: PathMarkGroup[]): PathTreeNode[] {
  const roots: Map<string, PathTreeNode> = new Map();

  for (const group of groups) {
    const path = group.path;
    // Normalize path separators
    const normalizedPath = path.replace(/\\/g, "/");
    const parts = normalizedPath.split("/").filter(Boolean);

    if (parts.length === 0) continue;

    // Find the root (first part with drive letter on Windows or first segment)
    let rootKey: string;
    let startIndex: number;

    // Handle Windows paths like "C:/..."
    if (parts[0].match(/^[A-Za-z]:$/)) {
      rootKey = parts[0];
      startIndex = 1;
    } else {
      // Unix paths or relative paths
      rootKey = normalizedPath.startsWith("/") ? "/" + parts[0] : parts[0];
      startIndex = 1;
    }

    if (!roots.has(rootKey)) {
      roots.set(rootKey, {
        name: rootKey,
        fullPath: rootKey,
        marks: [],
        children: new Map(),
      });
    }

    let currentNode = roots.get(rootKey)!;
    let currentPath = rootKey;

    // Traverse the path
    for (let i = startIndex; i < parts.length; i++) {
      const part = parts[i];
      currentPath = currentPath + "/" + part;

      if (!currentNode.children.has(part)) {
        currentNode.children.set(part, {
          name: part,
          fullPath: currentPath,
          marks: [],
          children: new Map(),
        });
      }

      currentNode = currentNode.children.get(part)!;
    }

    // Add marks and exists status to the final node
    currentNode.marks = group.marks;
    currentNode.exists = group.exists;
  }

  return Array.from(roots.values());
}

// Collapse single-child paths into one line
function collapseSingleChildPaths(node: PathTreeNode): PathTreeNode {
  const children = Array.from(node.children.values());

  // First, recursively process all children
  const collapsedChildren = children.map(collapseSingleChildPaths);

  // If this node has exactly one child and no marks, merge with child
  if (collapsedChildren.length === 1 && node.marks.length === 0) {
    const child = collapsedChildren[0];
    return {
      name: node.name + "/" + child.name,
      fullPath: child.fullPath,
      marks: child.marks,
      children: child.children,
      exists: child.exists,
    };
  }

  // Otherwise, update children
  const newChildren = new Map<string, PathTreeNode>();
  for (const child of collapsedChildren) {
    newChildren.set(child.name, child);
  }

  return {
    ...node,
    children: newChildren,
  };
}

interface TreeNodeComponentProps {
  node: PathTreeNode;
  depth: number;
  onSaveMark: (
    path: string,
    mark: BakabaseAbstractionsModelsDomainPathMark,
    oldMark?: BakabaseAbstractionsModelsDomainPathMark,
  ) => void;
  onDeleteMark: (path: string, mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onConfigurePath: (path: string) => void;
  onTransferMarks: (fromPath: string) => void;
  onDeletePathMarks: (path: string) => void;
  onPasteMarks: (path: string, marks: BakabaseAbstractionsModelsDomainPathMark[]) => void;
}

const TreeNodeComponent = ({
  node,
  depth,
  onSaveMark,
  onDeleteMark,
  onConfigurePath,
  onTransferMarks,
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

  const handleConfigurePath = useCallback(() => {
    onConfigurePath(node.fullPath);
  }, [node.fullPath, onConfigurePath]);

  const handleTransferMarks = useCallback(() => {
    onTransferMarks(node.fullPath);
  }, [node.fullPath, onTransferMarks]);

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
      toast.error(t("Failed to start sync"));
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
            {/* Configure button - only for valid paths */}
            {!isInvalid && (
              <Tooltip content={t("Configure marks")}>
                <Button
                  isIconOnly
                  className="min-w-0 w-6 h-6"
                  color="primary"
                  size="sm"
                  variant="light"
                  onPress={handleConfigurePath}
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

            {/* Copy button - only for paths with marks */}
            {hasMarks && (
              <Tooltip content={t("Copy marks from this path")}>
                <Button
                  isIconOnly
                  className="min-w-0 w-6 h-6"
                  color="default"
                  size="sm"
                  variant="light"
                  onPress={handleEnterCopyMode}
                >
                  <AiOutlineCopy className="text-sm" />
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

            {/* Transfer button - for all paths */}
            <Tooltip content={t("Transfer marks to another path")}>
              <Button
                isIconOnly
                className="min-w-0 w-6 h-6"
                color="warning"
                size="sm"
                variant="light"
                onPress={handleTransferMarks}
              >
                <AiOutlineSwap className="text-sm" />
              </Button>
            </Tooltip>

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
                onConfigurePath={onConfigurePath}
                onDeleteMark={onDeleteMark}
                onDeletePathMarks={onDeletePathMarks}
                onPasteMarks={onPasteMarks}
                onSaveMark={onSaveMark}
                onTransferMarks={onTransferMarks}
              />
            ))}
        </div>
      )}
    </div>
  );
};

const PathTree = ({
  groups,
  onSaveMark,
  onDeleteMark,
  onConfigurePath,
  onTransferMarks,
  onDeletePathMarks,
  onPasteMarks,
}: PathTreeProps) => {
  const { t } = useTranslation();

  const treeNodes = useMemo(() => {
    const rawTree = buildPathTree(groups);
    return rawTree.map(collapseSingleChildPaths).sort((a, b) => a.name.localeCompare(b.name));
  }, [groups]);

  if (treeNodes.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-64 text-default-500">
        <AiOutlineFolder className="text-4xl mb-2" />
        <p>{t("No path marks found")}</p>
        <p className="text-sm">{t("Go to path mark config page to add marks")}</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      {treeNodes.map((node) => (
        <TreeNodeComponent
          key={node.fullPath}
          depth={0}
          node={node}
          onConfigurePath={onConfigurePath}
          onDeleteMark={onDeleteMark}
          onDeletePathMarks={onDeletePathMarks}
          onPasteMarks={onPasteMarks}
          onSaveMark={onSaveMark}
          onTransferMarks={onTransferMarks}
        />
      ))}
    </div>
  );
};

PathTree.displayName = "PathTree";

export default PathTree;
