"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { PathMarkGroup } from "@/pages/path-mark-config/hooks/usePathMarks";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineFolder } from "react-icons/ai";

import TreeNodeComponent from "./TreeNodeComponent";
import type { PathTreeNode } from "./TreeNodeComponent";

interface PathTreeProps {
  groups: PathMarkGroup[];
  onSaveMark: (
    path: string,
    mark: BakabaseAbstractionsModelsDomainPathMark,
    oldMark?: BakabaseAbstractionsModelsDomainPathMark,
  ) => void;
  onDeleteMark: (path: string, mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onConfigureSubPathMarks: (path: string) => void; // Open config modal for valid paths
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

const PathTree = ({
  groups,
  onSaveMark,
  onDeleteMark,
  onConfigureSubPathMarks,
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
        <p>{t("pathMarks.empty.noPathMarks")}</p>
        <p className="text-sm">{t("pathMarks.empty.goToConfigPage")}</p>
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
          onConfigureSubPathMarks={onConfigureSubPathMarks}
          onDeleteMark={onDeleteMark}
          onDeletePathMarks={onDeletePathMarks}
          onPasteMarks={onPasteMarks}
          onSaveMark={onSaveMark}
        />
      ))}
    </div>
  );
};

PathTree.displayName = "PathTree";

export default PathTree;
