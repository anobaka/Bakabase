"use client";

import React, { useCallback, useMemo } from "react";
import { useTranslation } from "react-i18next";
import {
  Checkbox,
  Chip,
} from "@/components/bakaui";
import type {
  BakabaseModulesAIServicesFileOperation,
} from "@/sdk/Api";
import { FileOperationType, FileOperationTypeLabel } from "@/sdk/constants";

interface TreeNode {
  operation: BakabaseModulesAIServicesFileOperation;
  index: number;
  children: TreeNode[];
}

export interface FileOperationTreeProps {
  operations: BakabaseModulesAIServicesFileOperation[];
  selectedIndices: Set<number>;
  onSelectionChange: (newSelection: Set<number>) => void;
}

/**
 * Build a tree structure from flat operations using dependsOnOrder.
 * Operations without dependsOnOrder are root nodes.
 */
function buildTree(operations: BakabaseModulesAIServicesFileOperation[]): TreeNode[] {
  const nodeMap = new Map<number, TreeNode>();
  const roots: TreeNode[] = [];

  // Create nodes
  operations.forEach((op, index) => {
    nodeMap.set(op.order, { operation: op, index, children: [] });
  });

  // Build tree
  operations.forEach((op) => {
    const node = nodeMap.get(op.order)!;
    if (op.dependsOnOrder != null) {
      const parent = nodeMap.get(op.dependsOnOrder);
      if (parent) {
        parent.children.push(node);
        return;
      }
    }
    roots.push(node);
  });

  return roots;
}

/**
 * Get all descendant indices of a node (including the node itself).
 */
function getAllDescendantIndices(node: TreeNode): number[] {
  const indices: number[] = [node.index];
  for (const child of node.children) {
    indices.push(...getAllDescendantIndices(child));
  }
  return indices;
}

/**
 * Get all ancestor indices of a node by order, walking up the tree.
 */
function getAncestorIndices(
  order: number | undefined,
  orderToNode: Map<number, TreeNode>,
  operations: BakabaseModulesAIServicesFileOperation[],
): number[] {
  const ancestors: number[] = [];
  let currentOrder = order;
  while (currentOrder != null) {
    const node = orderToNode.get(currentOrder);
    if (!node) break;
    ancestors.push(node.index);
    currentOrder = node.operation.dependsOnOrder ?? undefined;
  }
  return ancestors;
}

const FileOperationTree = ({ operations, selectedIndices, onSelectionChange }: FileOperationTreeProps) => {
  const { t } = useTranslation();

  const { roots, orderToNode } = useMemo(() => {
    const roots = buildTree(operations);
    const orderToNode = new Map<number, TreeNode>();
    operations.forEach((op, index) => {
      orderToNode.set(op.order, { operation: op, index, children: [] });
    });
    // Rebuild with proper children references
    const properMap = new Map<number, TreeNode>();
    const buildProperMap = (nodes: TreeNode[]) => {
      for (const node of nodes) {
        properMap.set(node.operation.order, node);
        buildProperMap(node.children);
      }
    };
    buildProperMap(roots);
    return { roots, orderToNode: properMap };
  }, [operations]);

  const handleToggle = useCallback((node: TreeNode) => {
    const newSelection = new Set(selectedIndices);
    const isCurrentlySelected = selectedIndices.has(node.index);

    if (isCurrentlySelected) {
      // Deselect: remove this node and all descendants
      const toRemove = getAllDescendantIndices(node);
      for (const idx of toRemove) {
        newSelection.delete(idx);
      }
    } else {
      // Select: add this node and all ancestors
      newSelection.add(node.index);
      const ancestors = getAncestorIndices(
        node.operation.dependsOnOrder ?? undefined,
        orderToNode,
        operations,
      );
      for (const idx of ancestors) {
        newSelection.add(idx);
      }
    }

    onSelectionChange(newSelection);
  }, [selectedIndices, onSelectionChange, orderToNode, operations]);

  const renderNode = (node: TreeNode, depth: number) => {
    const { operation, index } = node;
    const isSelected = selectedIndices.has(index);

    return (
      <div key={operation.order}>
        <div
          className="flex items-start gap-2 p-2 rounded bg-default-50 hover:bg-default-100 cursor-pointer"
          style={{ marginLeft: depth * 24 }}
          onClick={() => handleToggle(node)}
        >
          <Checkbox
            size="sm"
            isSelected={isSelected}
            onValueChange={() => handleToggle(node)}
            className="mt-0.5"
          />
          <div className="flex flex-col gap-0.5 min-w-0 flex-1">
            <div className="flex items-center gap-2 text-sm flex-wrap">
              <Chip size="sm" variant="flat" color={
                operation.type === FileOperationType.Rename ? "primary" :
                operation.type === FileOperationType.Move ? "secondary" :
                "success"
              }>
                {t(`fileProcessor.ai.opType.${FileOperationTypeLabel[operation.type as FileOperationType]}`)}
              </Chip>
              <span className="text-default-400 truncate" title={operation.sourcePath}>{operation.sourcePath}</span>
              {operation.type !== FileOperationType.CreateDirectory && (
                <>
                  <span className="text-default-300">&rarr;</span>
                  <span className="text-primary font-medium truncate" title={operation.destinationPath}>{operation.destinationPath}</span>
                </>
              )}
            </div>
            {operation.reason && <span className="text-xs text-default-400">{operation.reason}</span>}
          </div>
        </div>
        {node.children.length > 0 && (
          <div className="flex flex-col gap-1 mt-1">
            {node.children.map((child) => renderNode(child, depth + 1))}
          </div>
        )}
      </div>
    );
  };

  if (!operations || operations.length === 0) return null;

  return (
    <div className="flex flex-col gap-1">
      {roots.map((node) => renderNode(node, 0))}
    </div>
  );
};

FileOperationTree.displayName = "FileOperationTree";

export default FileOperationTree;
