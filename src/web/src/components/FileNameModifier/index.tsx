"use client";


import React, { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineEdit,
  AiOutlineEye,
  AiOutlineEyeInvisible,
  AiOutlinePlusCircle,
  AiOutlineUndo,
} from "react-icons/ai";
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  DragEndEvent,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";

import { Button, Textarea, Modal, Card } from "../bakaui";

import SortableOperationCard from "./SortableOperationCard";
import PreviewList from "./PreviewList";
import { useFileNameModifier, OperationWithId } from "./useFileNameModifier";

import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

export interface FileNameModificationResult {
  originalPath: string;
  modifiedPath: string;
  originalFileName: string;
  modifiedFileName: string;
  commonPrefix: string;
  originalRelative: string;
  modifiedRelative: string;
}

let operationIdCounter = 0;
const generateOperationId = () => `op-${Date.now()}-${operationIdCounter++}`;

const createDefaultOperation = (): OperationWithId => ({
  id: generateOperationId(),
  target: 2, // FileNameWithoutExtension
  operation: 1,
  position: 1,
  positionIndex: 0,
  targetText: "",
  text: "",
  deleteCount: 0,
  deleteStartPosition: 0,
  caseType: 1,
  dateTimeFormat: "",
  alphabetStartChar: "A",
  alphabetCount: 0,
  replaceEntire: false,
});

interface FileNameModifierProps {
  initialFilePaths?: string[];
  onClose?: () => void;
}

import { validateOperation } from "./validation";
import { detectCommonPrefix } from "./utils";

const FileNameModifier: React.FC<FileNameModifierProps> = ({
  initialFilePaths = [],
  onClose,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    operations,
    setOperations,
    filePaths,
    setFilePaths,
    previewResults,
    setPreviewResults,
    error,
    setError,
    isPreviewLoading,
    setIsPreviewLoading,
    lastFilePaths,
    setLastFilePaths,
  } = useFileNameModifier(initialFilePaths);

  const [showTextarea, setShowTextarea] = useState(false);
  const [showFullPaths, setShowFullPaths] = useState(false);
  const [modifying, setModifying] = useState(false);

  // 拖拽排序 sensors
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  // 拖拽结束处理
  const handleDragEnd = useCallback(
    (event: DragEndEvent) => {
      const { active, over } = event;
      if (over && active.id !== over.id) {
        setOperations((ops) => {
          const oldIndex = ops.findIndex((op) => op.id === active.id);
          const newIndex = ops.findIndex((op) => op.id === over.id);
          return arrayMove(ops, oldIndex, newIndex);
        });
      }
    },
    [setOperations]
  );

  // 操作项增删改、复制
  const handleOperationChange = useCallback(
    (id: string, op: OperationWithId) => {
      setOperations((ops) => ops.map((item) => (item.id === id ? op : item)));
    },
    [setOperations]
  );

  const handleOperationDelete = useCallback(
    (id: string) => {
      setOperations((ops) => ops.filter((op) => op.id !== id));
    },
    [setOperations]
  );

  const handleOperationCopy = useCallback(
    (id: string) => {
      setOperations((ops) => {
        const idx = ops.findIndex((op) => op.id === id);
        if (idx === -1) return ops;
        const next = [...ops];
        const copy: OperationWithId = {
          ...ops[idx],
          id: generateOperationId(),
        };
        next.splice(idx + 1, 0, copy);
        return next;
      });
    },
    [setOperations]
  );

  const handleAddOperation = useCallback(() => {
    setOperations((ops) => [...ops, createDefaultOperation()]);
  }, [setOperations]);

  // 撤销功能：恢复上次的文件路径
  const handleRestoreFilePaths = useCallback(() => {
    if (lastFilePaths) {
      setFilePaths(lastFilePaths);
      setLastFilePaths(null);
    }
  }, [lastFilePaths, setFilePaths, setLastFilePaths]);

  // 文件路径输入
  const handleConfirmPaths = () => {
    const paths = filePaths
      .join("\n")
      .split("\n")
      .map((f) => f.trim())
      .filter(Boolean);

    setFilePaths(paths);
    setShowTextarea(false);
  };
  const handleShowFileListEdit = () => {
    setShowTextarea(true);
  };
  // 新增：去重、清空、粘贴
  const handleDeduplicatePaths = () => {
    setFilePaths((paths) =>
      Array.from(new Set(paths.map((f) => f.trim()).filter(Boolean))),
    );
  };
  // 预览区公共前缀
  const commonPrefix = useMemo(
    () => detectCommonPrefix(previewResults.map((r) => r.originalPath)),
    [previewResults]
  );

  // 检查预览结果中是否有任何变更
  const hasAnyChanges = useMemo(
    () => previewResults.some((r) => r.originalPath !== r.modifiedPath),
    [previewResults]
  );

  const debounceTimer = useRef<NodeJS.Timeout | null>(null);

  useEffect(() => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);

    if (filePaths.length === 0) {
      setPreviewResults([]);
      return;
    }
    // 只用合法操作预览
    const validOperations = operations.filter((op) => !validateOperation(op));

    debounceTimer.current = setTimeout(() => {
      (async () => {
        try {
          setError("");
          setIsPreviewLoading(true);
          const rsp = await BApi.fileNameModifier.previewFileNameModification({
            filePaths,
            operations: validOperations,
          });
          let modifiedPaths: string[] = rsp.data ?? [];
          // 构建 previewResults
          const results = filePaths.map((originalPath, i) => {
            const modifiedPath = modifiedPaths[i] || originalPath;
            const getFileName = (p: string) => p.split(/[\\/]/).pop() || "";

            return {
              originalPath,
              modifiedPath,
              originalFileName: getFileName(originalPath),
              modifiedFileName: getFileName(modifiedPath),
              commonPrefix: "",
              originalRelative: "",
              modifiedRelative: "",
            };
          });

          setPreviewResults(results);
        } catch (e: any) {
          setError(e?.message || t<string>("FileNameModifier.PreviewFailed"));
        } finally {
          setIsPreviewLoading(false);
        }
      })();
    }, 300);

    // 清理定时器
    return () => {
      if (debounceTimer.current) clearTimeout(debounceTimer.current);
    };
  }, [filePaths, operations]);

  const handleExecuteModification = async () => {
    const validOperations = operations.filter((op) => !validateOperation(op));

    if (validOperations.length === 0) {
      setError(t<string>("FileNameModifier.Error.NoValidOperation"));
      return;
    }

    try {
      setModifying(true);
      setError("");

      // 保存当前文件路径用于撤销
      setLastFilePaths([...filePaths]);

      const rsp = await BApi.fileNameModifier.modifyFileNames({
        filePaths,
        operations: validOperations,
      });
      const result = rsp.data ?? [];

      // 更新文件路径为新路径
      const newFilePaths = filePaths.map((oldPath) => {
        const resultItem = result.find((r) => r.oldPath === oldPath);
        return resultItem?.success && resultItem.newPath ? resultItem.newPath : oldPath;
      });
      setFilePaths(newFilePaths);

      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>("FileNameModifier.ModificationResult"),
        size: "xl",
        footer: {
          actions: ["cancel"],
        },
        children: (
          <>
            <div className="mb-2">
              <span className="text-green-600 font-semibold mr-4">
                {t<string>("FileNameModifier.ModificationSuccessCount", {
                  count: result.filter((r) => r.success).length,
                })}
              </span>
              <span className="text-red-600 font-semibold">
                {t<string>("FileNameModifier.ModificationFailCount", {
                  count: result.filter((r) => !r.success).length,
                })}
              </span>
            </div>
            {result.filter((r) => !r.success).length > 0 && (
              <div className="max-h-48 overflow-y-auto border rounded p-2 bg-background border border-default">
                <div className="font-semibold mb-1">
                  {t<string>("FileNameModifier.ModificationFailList")}
                </div>
                <ul className="text-xs">
                  {result
                    .filter((r) => !r.success)
                    .map((item) => (
                      <li key={item.oldPath} className="mb-1">
                        <span className="text-foreground">{item.oldPath}</span>
                        <span className="text-red-500 ml-2">
                          {t<string>("FileNameModifier.ModificationFailReason")}
                          : {item.error}
                        </span>
                      </li>
                    ))}
                </ul>
              </div>
            )}
          </>
        ),
      });

      setModifying(false);
    } catch (e: any) {
      setModifying(false);
      setLastFilePaths(null); // 失败时清除撤销状态
      setError(e?.message || t<string>("FileNameModifier.ModificationFailed"));
    }
  };

  return (
    <div className="flex flex-col min-h-0 grow md:flex-row gap-4">
      {/* 左侧：操作配置区域 */}
      <Card className="flex-1 flex flex-col min-w-0 p-4">
        <h5 className="font-semibold mb-2">
          {t<string>("FileNameModifier.OperationsList")}
        </h5>
        <div className="flex-1 overflow-y-auto rounded p-2">
          {operations.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-8 text-gray-400">
              <AiOutlinePlusCircle className="text-4xl mb-2" />
              <p className="text-sm mb-3">{t<string>("FileNameModifier.EmptyOperationsHint")}</p>
              <Button color="primary" variant="flat" onClick={handleAddOperation}>
                {t<string>("FileNameModifier.AddFirstOperation")}
              </Button>
            </div>
          ) : (
            <DndContext
              collisionDetection={closestCenter}
              sensors={sensors}
              onDragEnd={handleDragEnd}
            >
              <SortableContext
                items={operations.map((op) => op.id)}
                strategy={verticalListSortingStrategy}
              >
                {operations.map((op, idx) => {
                  const validationError = validateOperation(op);
                  return (
                    <SortableOperationCard
                      key={op.id}
                      errors={validationError ? t<string>(validationError) : ""}
                      id={op.id}
                      index={idx}
                      operation={op}
                      onChange={(op2) => handleOperationChange(op.id, { ...op2, id: op.id })}
                      onCopy={() => handleOperationCopy(op.id)}
                      onDelete={() => handleOperationDelete(op.id)}
                    />
                  );
                })}
              </SortableContext>
            </DndContext>
          )}
          {operations.length > 0 && (
            <Button
              aria-label={t<string>("FileNameModifier.AddOperation")}
              className="w-full mt-2"
              variant="light"
              onClick={handleAddOperation}
            >
              <AiOutlinePlusCircle className="text-lg" />
              {t<string>("FileNameModifier.AddOperation")}
            </Button>
          )}
        </div>
        {/* 操作按钮 */}
        <div className="mt-4">
          <div className="flex gap-2">
            <Button
              aria-label={t<string>("FileNameModifier.ExecuteModification")}
              color="primary"
              isDisabled={!hasAnyChanges || isPreviewLoading}
              isLoading={modifying}
              variant="solid"
              onClick={handleExecuteModification}
            >
              {t<string>("FileNameModifier.ExecuteModification")}
            </Button>
            {lastFilePaths && (
              <Button
                aria-label={t<string>("FileNameModifier.RestoreOriginalPaths")}
                color="warning"
                variant="flat"
                onClick={handleRestoreFilePaths}
              >
                <AiOutlineUndo className="text-lg" />
                {t<string>("FileNameModifier.RestoreOriginalPaths")}
              </Button>
            )}
          </div>
          {error && <div className="text-red-500 text-xs mt-2">{error}</div>}
        </div>
      </Card>
      {/* 右侧：文件路径输入/预览区域 */}
      <Card className="flex-1 flex flex-col min-w-0 p-4">
        <div className="mb-2 flex justify-between items-center">
          <h5 className="font-semibold mb-0 flex items-center gap-1">
            {showTextarea
              ? t<string>("FileNameModifier.EditFileList")
              : t<string>("FileNameModifier.PreviewResults")}
            {!showTextarea && (
              <Button
                aria-label={
                  showFullPaths
                    ? t<string>("FileNameModifier.HideFullPaths")
                    : t<string>("FileNameModifier.ShowFullPaths")
                }
                className="text-xs"
                size="sm"
                variant="light"
                onClick={() => setShowFullPaths(!showFullPaths)}
              >
                {showFullPaths ? (
                  <AiOutlineEyeInvisible className="text-base" />
                ) : (
                  <AiOutlineEye className="text-base" />
                )}
                {showFullPaths
                  ? t<string>("FileNameModifier.HideFullPaths")
                  : t<string>("FileNameModifier.ShowFullPaths")}
              </Button>
            )}
          </h5>
          {!showTextarea && (
            <Button
              aria-label={t<string>("FileNameModifier.EditFileList")}
              size="sm"
              variant="light"
              onClick={handleShowFileListEdit}
            >
              <AiOutlineEdit className="text-base" />
              {t<string>("FileNameModifier.EditFileList")}
            </Button>
          )}
        </div>
        <div className="flex-1 rounded min-h-0">
          {showTextarea ? (
            <div className="h-full flex flex-col min-h-0">
              <Textarea
                aria-label={t<string>("FileNameModifier.FilePathsTextarea")}
                maxRows={15}
                minRows={10}
                placeholder={t<string>("FileNameModifier.FilePathsPlaceholder")}
                value={filePaths.join("\n")}
                onValueChange={(e) => setFilePaths(e.split("\n"))}
              />
              <div className="mt-2 flex flex-wrap gap-2">
                <Button
                  aria-label={t<string>("FileNameModifier.ConfirmPaths")}
                  color="primary"
                  size="sm"
                  variant="solid"
                  onClick={handleConfirmPaths}
                >
                  {t<string>("FileNameModifier.ConfirmPaths")}
                </Button>
                <Button
                  aria-label={t<string>("FileNameModifier.Deduplicate")}
                  color="secondary"
                  size="sm"
                  variant="light"
                  onClick={handleDeduplicatePaths}
                >
                  {t<string>("FileNameModifier.Deduplicate")}
                </Button>
                <Button
                  aria-label={t<string>("FileNameModifier.Cancel")}
                  size="sm"
                  variant="flat"
                  onClick={() => setShowTextarea(false)}
                >
                  {t<string>("FileNameModifier.Cancel")}
                </Button>
              </div>
            </div>
          ) : (
            <PreviewList
              commonPrefix={commonPrefix}
              isLoading={isPreviewLoading}
              results={previewResults}
              showFullPaths={showFullPaths}
            />
          )}
        </div>
      </Card>
    </div>
  );
};

export default FileNameModifier;
