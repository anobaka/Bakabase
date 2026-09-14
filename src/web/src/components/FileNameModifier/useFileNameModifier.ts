import type {
  BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation as Operation,
  BakabaseServiceModelsViewFileRenameResult as RenameResult,
} from "@/sdk/Api";
import type { FileNameModificationResult } from "./index";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { validateOperation } from "./validation";

import BApi from "@/sdk/BApi";

export type { FileNameModificationResult };
export interface OperationWithId extends Operation {
  id: string;
}
interface Input {
  filePaths: string[];
  operations: Operation[];
}
interface Preview {
  key: string;
  input: Input;
  results: FileNameModificationResult[];
  error?: string;
}

function toPreview(filePaths: string[], modifiedPaths = filePaths): FileNameModificationResult[] {
  const fileName = (path: string) => path.split(/[\\/]/).pop() || "";

  return filePaths.map((originalPath, index) => ({
    originalPath,
    modifiedPath: modifiedPaths[index],
    originalFileName: fileName(originalPath),
    modifiedFileName: fileName(modifiedPaths[index]),
    commonPrefix: "",
    originalRelative: "",
    modifiedRelative: "",
  }));
}

export function normalizeFilePaths(paths: string[]) {
  return paths.map((path) => path.trim()).filter(Boolean);
}

export function useFileNameModifier(initialFilePaths: string[] = []) {
  const { t } = useTranslation();
  const [operations, setOperations] = useState<OperationWithId[]>([]);
  const [filePaths, setFilePaths] = useState<string[]>(() => normalizeFilePaths(initialFilePaths));
  const [lastFilePaths, setLastFilePaths] = useState<string[] | null>(null);
  const [preview, setPreview] = useState<Preview>();
  const [executionError, setExecutionError] = useState("");
  const [modifying, setModifying] = useState(false);
  const [previewRevision, setPreviewRevision] = useState(0);
  const modifyingRef = useRef(false);
  const initializedRef = useRef(initialFilePaths.length > 0);
  const input = useMemo<Input>(
    () => ({ filePaths, operations: operations.map(({ id: _id, ...operation }) => operation) }),
    [filePaths, operations],
  );
  const inputKey = JSON.stringify(input);
  const validationErrors = operations.map(validateOperation);
  const hasInvalidOperations = validationErrors.some(Boolean);
  const shouldPreview = filePaths.length > 0 && operations.length > 0 && !hasInvalidOperations;
  const currentPreview = preview?.key === inputKey ? preview : undefined;
  const previewResults = currentPreview?.results ?? toPreview(filePaths);
  const isPreviewLoading = shouldPreview && !currentPreview;
  const changedCount =
    currentPreview?.results.filter((item) => item.originalPath !== item.modifiedPath).length ?? 0;
  const canExecute =
    shouldPreview && !currentPreview?.error && !!currentPreview && changedCount > 0 && !modifying;
  const error = executionError || currentPreview?.error || "";

  useEffect(() => {
    if (!initializedRef.current && initialFilePaths.length > 0) {
      initializedRef.current = true;
      setFilePaths(normalizeFilePaths(initialFilePaths));
    }
  }, [initialFilePaths]);

  useEffect(() => {
    setExecutionError("");
  }, [inputKey]);

  useEffect(() => {
    let active = true;

    if (!shouldPreview || modifying) return;
    const timer = window.setTimeout(async () => {
      try {
        const response = await BApi.fileNameModifier.previewFileNameModification(input);

        if (!active) return;
        if (response.code || response.data?.length !== input.filePaths.length)
          throw new Error(response.message || t<string>("FileNameModifier.PreviewFailed"));
        setPreview({ key: inputKey, input, results: toPreview(input.filePaths, response.data) });
      } catch (failure) {
        if (active)
          setPreview({
            key: inputKey,
            input,
            results: toPreview(input.filePaths),
            error:
              failure instanceof Error
                ? failure.message
                : t<string>("FileNameModifier.PreviewFailed"),
          });
      }
    }, 300);

    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [input, inputKey, shouldPreview, modifying, previewRevision]);

  const refreshPreview = () => {
    setPreview(undefined);
    setPreviewRevision((revision) => revision + 1);
  };
  const execute = useCallback(async (): Promise<RenameResult[] | undefined> => {
    // The displayed preview belongs to one exact input. A draft edit invalidates it immediately,
    // including the debounce window before its replacement request has begun.
    if (!canExecute || !currentPreview || modifyingRef.current) return;
    const snapshot = currentPreview.input;

    modifyingRef.current = true;
    setModifying(true);
    setExecutionError("");
    try {
      const response = await BApi.fileNameModifier.modifyFileNames(snapshot);

      if (response.code || response.data?.length !== snapshot.filePaths.length)
        throw new Error(response.message || t<string>("FileNameModifier.ModificationFailed"));
      const results = response.data;

      setLastFilePaths([...snapshot.filePaths]);
      setFilePaths(
        snapshot.filePaths.map((path) => {
          const result = results.find((item) => item.oldPath === path);

          return result?.success && result.newPath ? result.newPath : path;
        }),
      );
      setPreview(undefined);

      return results;
    } catch (failure) {
      setExecutionError(
        failure instanceof Error
          ? failure.message
          : t<string>("FileNameModifier.ModificationFailed"),
      );
    } finally {
      modifyingRef.current = false;
      setModifying(false);
    }
  }, [canExecute, currentPreview, t]);

  return {
    operations,
    setOperations,
    filePaths,
    setFilePaths,
    lastFilePaths,
    setLastFilePaths,
    previewResults,
    isPreviewLoading,
    hasInvalidOperations,
    validationErrors,
    changedCount,
    canExecute,
    modifying,
    error,
    execute,
    refreshPreview,
  };
}
