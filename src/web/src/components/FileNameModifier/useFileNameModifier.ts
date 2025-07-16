import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "../../sdk/Api";
import type { FileNameModificationResult } from "./index";

import { useState, useEffect } from "react";

export type { FileNameModificationResult };

export function useFileNameModifier(initialFilePaths: string[] = []) {
  const [operations, setOperations] = useState<
    BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation[]
  >([]);
  const [filePaths, setFilePaths] = useState<string[]>(initialFilePaths);
  const [previewResults, setPreviewResults] = useState<
    FileNameModificationResult[]
  >([]);
  const [error, setError] = useState<string>("");

  // 自动初始化预览
  useEffect(() => {
    if (initialFilePaths.length > 0 && filePaths.length === 0) {
      setFilePaths(initialFilePaths);
    }
  }, [initialFilePaths, filePaths.length]);

  // ... 其它方法

  return {
    operations,
    setOperations,
    filePaths,
    setFilePaths,
    previewResults,
    setPreviewResults,
    error,
    setError,
    // ... 其它操作方法
  };
}
