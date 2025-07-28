"use client";

import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "@/sdk/Api";

import React, { useState, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineEdit,
  AiOutlineEye,
  AiOutlineEyeInvisible,
  AiOutlinePlusCircle,
} from "react-icons/ai";

import { Button, Textarea, Modal, Card } from "../bakaui";

import OperationCard from "./OperationCard";
import PreviewList from "./PreviewList";
import { useFileNameModifier } from "./useFileNameModifier";

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

const defaultOperation: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation =
  {
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
  };

interface FileNameModifierProps {
  initialFilePaths?: string[];
  onClose?: () => void;
}

// 校验函数，返回 i18n key
function validateOperation(
  op: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation,
): string {
  if (!op.target) return "FileNameModifier.Error.TargetRequired";
  if (!op.operation) return "FileNameModifier.Error.OperationTypeRequired";
  switch (op.operation) {
    case 1: // Insert
      if (!op.text && !op.targetText)
        return "FileNameModifier.Error.InsertTextRequired";
      break;
    case 2: // AddDateTime
      if (!op.dateTimeFormat)
        return "FileNameModifier.Error.DateTimeFormatRequired";
      break;
    case 3: // Delete
      if (
        op.deleteCount == null ||
        op.deleteStartPosition == null ||
        !op.position
      )
        return "FileNameModifier.Error.DeleteParamsRequired";
      break;
    case 4: // Replace
      if (!op.text && !op.targetText)
        return "FileNameModifier.Error.ReplaceTextRequired";
      break;
    case 5: // ChangeCase
      if (!op.caseType) return "FileNameModifier.Error.CaseTypeRequired";
      break;
    case 6: // AddAlphabetSequence
      if (!op.alphabetStartChar || op.alphabetCount == null)
        return "FileNameModifier.Error.AlphabetParamsRequired";
      break;
    case 7: // Reverse
      break;
    default:
      return "FileNameModifier.Error.UnknownOperationType";
  }

  return "";
}

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
  } = useFileNameModifier(initialFilePaths);

  // 折叠/展开状态
  const [expandedItems, setExpandedItems] = useState<Set<number>>(new Set());
  const [showTextarea, setShowTextarea] = useState(false);
  const [showFullPaths, setShowFullPaths] = useState(false);
  const [modifying, setModifying] = useState(false);

  // 操作项增删改、移动、复制
  const handleOperationChange = (idx: number, op) => {
    setOperations((ops) => ops.map((item, i) => (i === idx ? op : item)));
  };
  const handleOperationDelete = (idx: number) => {
    setOperations((ops) => ops.filter((_, i) => i !== idx));
  };
  const handleOperationMoveUp = (idx: number) => {
    setOperations((ops) => {
      if (idx === 0) return ops;
      const next = [...ops];

      if (next[idx] && next[idx - 1]) {
        const temp = {
          ...next[idx - 1],
        } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;

        next[idx - 1] = {
          ...next[idx],
        } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
        next[idx] = temp;
      }

      return next;
    });
  };
  const handleOperationMoveDown = (idx: number) => {
    setOperations((ops) => {
      if (idx === ops.length - 1) return ops;
      const next = [...ops];

      if (next[idx] && next[idx + 1]) {
        const temp = {
          ...next[idx + 1],
        } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;

        next[idx + 1] = {
          ...next[idx],
        } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
        next[idx] = temp;
      }

      return next;
    });
  };
  const handleOperationCopy = (idx: number) => {
    setOperations((ops) => {
      const next = [...ops];
      const copy = {
        ...ops[idx],
      } as BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;

      next.splice(idx + 1, 0, copy);

      return next;
    });
  };

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
  // 预览区主行/展开
  const commonPrefix = ""; // TODO: 用 utils.detectCommonPrefix(previewResults.map(r => r.originalPath))

  const debounceTimer = useRef<NodeJS.Timeout | null>(null);

  useEffect(() => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);

    if (filePaths.length === 0) {
      return;
    }
    // 只用合法操作预览
    const validOperations = operations.filter((op) => !validateOperation(op));

    debounceTimer.current = setTimeout(() => {
      (async () => {
        try {
          setError("");
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
        }
      })();
    }, 300);
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
      const rsp = await BApi.fileNameModifier.modifyFileNames({
        filePaths,
        operations: validOperations,
      });
      const result = rsp.data ?? [];

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
          {operations.map((op, idx) => (
            <OperationCard
              key={idx}
              aria-label={t<string>("FileNameModifier.OperationCardAria", {
                index: idx + 1,
              })}
              errors={
                validateOperation(op) ? t<string>(validateOperation(op)) : ""
              }
              index={idx}
              operation={op}
              onChange={(op2) => handleOperationChange(idx, op2)}
              onCopy={() => handleOperationCopy(idx)}
              onDelete={() => handleOperationDelete(idx)}
              onMoveDown={
                idx < operations.length - 1
                  ? () => handleOperationMoveDown(idx)
                  : undefined
              }
              onMoveUp={idx > 0 ? () => handleOperationMoveUp(idx) : undefined}
            />
          ))}
          <Button
            aria-label={t<string>("FileNameModifier.AddOperation")}
            className="w-full mt-2"
            variant="light"
            onClick={() =>
              setOperations((ops) => [...ops, { ...defaultOperation }])
            }
          >
            <AiOutlinePlusCircle className="text-lg" />
            {t<string>("FileNameModifier.AddOperation")}
          </Button>
        </div>
        {/* 操作按钮 */}
        <div className="mt-4">
          <div className="flex gap-2">
            <Button
              aria-label={t<string>("FileNameModifier.ExecuteModification")}
              color="primary"
              isLoading={modifying}
              variant="solid"
              onClick={handleExecuteModification}
            >
              {t<string>("FileNameModifier.ExecuteModification")}
            </Button>
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
