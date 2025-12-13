import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "../../sdk/Api";
import type { FileNameModificationResult } from "./index";

import { useState, useEffect, useRef } from "react";

export type { FileNameModificationResult };

// 带 id 的操作类型
export interface OperationWithId extends BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation {
  id: string;
}

export function useFileNameModifier(initialFilePaths: string[] = []) {
  const [operations, setOperations] = useState<OperationWithId[]>([]);
  const [filePaths, setFilePaths] = useState<string[]>(initialFilePaths);
  const [previewResults, setPreviewResults] = useState<
    FileNameModificationResult[]
  >([]);
  const [error, setError] = useState<string>("");
  const [isPreviewLoading, setIsPreviewLoading] = useState(false);
  const [lastFilePaths, setLastFilePaths] = useState<string[] | null>(null);

  // 追踪是否已初始化，避免清空后重新填充
  const initializedRef = useRef(false);

  // 自动初始化预览
  useEffect(() => {
    if (!initializedRef.current && initialFilePaths.length > 0) {
      setFilePaths(initialFilePaths);
      initializedRef.current = true;
    }
  }, [initialFilePaths]);

  return {
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
  };
}
