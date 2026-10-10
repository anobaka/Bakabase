import type {
  BakabaseAbstractionsModelsDomainPathMark,
  BakabaseAbstractionsModelsDomainConstantsPathMarkAdditionalItem,
} from "@/sdk/Api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { IwFsType, PathMarkSyncStatus, PathMarkAdditionalItem } from "@/sdk/constants";
import { usePathMarksStore } from "@/stores/pathMarks";
import { extractErrorMessage } from "@/components/utils";
import { storageError } from "@/stores/userStorage";

// Group marks by path
export interface PathMarkGroup {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  exists?: boolean; // undefined means not checked yet
  error?: string; // inaccessible or failed checks are not evidence of a missing path
}

/**
 * Hook to fetch and manage path marks
 */
export function usePathMarks({ checkExistence = true }: { checkExistence?: boolean } = {}) {
  const { t } = useTranslation();
  const [allMarks, setAllMarks] = useState<BakabaseAbstractionsModelsDomainPathMark[]>([]);
  const [allPaths, setAllPaths] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [pathExistsMap, setPathExistsMap] = useState<Map<string, boolean>>(new Map());
  const [pathErrorsMap, setPathErrorsMap] = useState<Map<string, string>>(new Map());
  const [checkingPaths, setCheckingPaths] = useState(false);
  const isInitialLoad = useRef(true);
  const pathCheck = useRef<AbortController>();

  useEffect(() => () => pathCheck.current?.abort(), []);

  // Load all path marks on mount
  useEffect(() => {
    loadAllMarks();
  }, []);

  const loadAllMarks = useCallback(async () => {
    // Only show loading on initial load, not on refresh
    if (isInitialLoad.current) {
      setLoading(true);
    }

    try {
      // Request both Property and MediaLibrary additional items
      // Cast to the API type since it's a flags enum and bitwise OR produces a value (3) not in the union type
      const additionalItems = (PathMarkAdditionalItem.Property |
        PathMarkAdditionalItem.MediaLibrary) as BakabaseAbstractionsModelsDomainConstantsPathMarkAdditionalItem;
      const rsp = await BApi.pathMark.getAllPathMarks({ additionalItems });

      if (!rsp.code && rsp.data) {
        setAllMarks(rsp.data);

        // Sync to zustand store so PathMarkChip components get updated immediately
        usePathMarksStore.getState().setMarks(rsp.data);

        // Extract unique paths
        const paths = [...new Set(rsp.data.map((m) => m.path).filter(Boolean))] as string[];

        setAllPaths(paths);
      }
    } catch (error) {
      console.error("Failed to load path marks:", error);
    } finally {
      if (isInitialLoad.current) {
        setLoading(false);
        isInitialLoad.current = false;
      }
    }
  }, []);

  // Check if paths exist on file system - only check new paths
  const checkPathsExistence = useCallback(
    async (paths: string[], existingMap: Map<string, boolean>, retry = false) => {
      // Find paths that haven't been checked yet
      const newPaths = retry ? paths : paths.filter((p) => !existingMap.has(p));

      if (newPaths.length === 0) return existingMap;

      pathCheck.current?.abort();
      const controller = new AbortController();

      pathCheck.current = controller;
      setCheckingPaths(true);

      // Check only new paths in parallel
      const results = await Promise.all(
        newPaths.map(async (path) => {
          try {
            const rsp = await BApi.file.getIwFsEntry(
              { path },
              { showErrorToast: false, signal: controller.signal },
            );

            if (rsp.code || !rsp.data)
              throw new Error(rsp.message || t("pathMarkConfig.pathCheck.failed"));
            // Path exists if we get a valid response with data and type is not Invalid
            const exists = !rsp.code && rsp.data != null && rsp.data.type !== IwFsType.Invalid;

            return { path, exists, error: undefined };
          } catch (error) {
            return {
              path,
              error: storageError(new Error(extractErrorMessage(error)), t).message,
              exists: undefined,
            };
          }
        }),
      );

      if (controller.signal.aborted) return existingMap;

      // Merge with existing map
      const newMap = new Map(existingMap);
      const errors = new Map<string, string>();

      for (const { path, exists, error } of results) {
        if (error) {
          newMap.delete(path);
          errors.set(path, error);
        } else if (exists !== undefined) newMap.set(path, exists);
      }

      // Remove paths that no longer exist in allPaths
      const pathSet = new Set(paths);

      for (const key of newMap.keys()) {
        if (!pathSet.has(key)) {
          newMap.delete(key);
        }
      }

      setPathExistsMap(newMap);
      setPathErrorsMap(errors);
      setCheckingPaths(false);

      return newMap;
    },
    [t],
  );

  // Check paths existence when allPaths changes
  useEffect(() => {
    const active = new Set(allPaths);

    setPathErrorsMap((previous) => new Map([...previous].filter(([path]) => active.has(path))));
    if (checkExistence && allPaths.length > 0) {
      checkPathsExistence(allPaths, pathExistsMap);
    } else if (allPaths.length === 0) {
      pathCheck.current?.abort();
      setPathExistsMap(new Map());
      setCheckingPaths(false);
    }
  }, [allPaths, checkExistence]);

  const retryPathChecks = useCallback(() => {
    void checkPathsExistence(allPaths, pathExistsMap, true);
  }, [allPaths, pathExistsMap, checkPathsExistence]);

  // Get count of invalid paths
  const getInvalidPathsCount = useCallback((): number => {
    let count = 0;

    for (const path of allPaths) {
      if (pathExistsMap.has(path) && !pathExistsMap.get(path)) {
        count++;
      }
    }

    return count;
  }, [allPaths, pathExistsMap]);

  /**
   * Get marks for a specific path (exact match only)
   */
  const getMarksForPath = useCallback(
    (path: string): BakabaseAbstractionsModelsDomainPathMark[] => {
      if (!path) return [];

      const normalizedPath = path.replace(/\\/g, "/").toLowerCase();

      return allMarks.filter((mark) => {
        const markPath = (mark.path || "").replace(/\\/g, "/").toLowerCase();

        return markPath === normalizedPath && mark.syncStatus !== PathMarkSyncStatus.PendingDelete;
      });
    },
    [allMarks],
  );

  /**
   * Get marks group that applies to a path (including inherited from parent paths)
   */
  const getApplicableMarksGroup = useCallback(
    (path: string): PathMarkGroup | null => {
      if (!path) return null;

      const normalizedPath = path.replace(/\\/g, "/").toLowerCase();

      // Find the most specific path that has marks
      let bestMatch: string | null = null;
      let bestMatchLength = 0;

      for (const markPath of allPaths) {
        const normalizedMarkPath = markPath.replace(/\\/g, "/").toLowerCase();

        if (
          normalizedPath.startsWith(normalizedMarkPath) ||
          normalizedPath === normalizedMarkPath
        ) {
          if (normalizedMarkPath.length > bestMatchLength) {
            bestMatch = markPath;
            bestMatchLength = normalizedMarkPath.length;
          }
        }
      }

      if (!bestMatch) return null;

      const marks = getMarksForPath(bestMatch);

      return marks.length > 0 ? { path: bestMatch, marks } : null;
    },
    [allPaths, getMarksForPath],
  );

  /**
   * Check if a path has marks (exact match)
   */
  const hasMarks = useCallback(
    (path: string): boolean => {
      return getMarksForPath(path).length > 0;
    },
    [getMarksForPath],
  );

  /**
   * Get marks grouped by path
   */
  const getGroupedMarks = useCallback((): PathMarkGroup[] => {
    const groups: Map<string, BakabaseAbstractionsModelsDomainPathMark[]> = new Map();

    for (const mark of allMarks) {
      if (mark.syncStatus === PathMarkSyncStatus.PendingDelete) continue;
      const path = mark.path || "Unknown";

      if (!groups.has(path)) {
        groups.set(path, []);
      }
      groups.get(path)!.push(mark);
    }

    return Array.from(groups.entries()).map(([path, marks]) => ({
      path,
      marks: marks.sort((a, b) => (a.priority || 0) - (b.priority || 0)),
      exists: pathExistsMap.get(path),
      error: pathErrorsMap.get(path),
    }));
  }, [allMarks, pathExistsMap, pathErrorsMap]);

  /**
   * Get marks grouped by path, filtered by existence
   */
  const getGroupedMarksFiltered = useCallback(
    (showOnlyInvalid: boolean): PathMarkGroup[] => {
      const groups = getGroupedMarks();

      if (!showOnlyInvalid) return groups;

      return groups.filter((group) => group.exists === false);
    },
    [getGroupedMarks],
  );

  return {
    allMarks,
    allPaths,
    loading,
    checkingPaths,
    pathExistsMap,
    pathErrorsMap,
    retryPathChecks,
    loadAllMarks,
    getMarksForPath,
    getApplicableMarksGroup,
    hasMarks,
    getGroupedMarks,
    getGroupedMarksFiltered,
    getInvalidPathsCount,
  };
}

export default usePathMarks;
