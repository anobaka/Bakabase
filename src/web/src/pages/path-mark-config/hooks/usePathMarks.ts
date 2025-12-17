import { useCallback, useEffect, useState } from "react";
import BApi from "@/sdk/BApi";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import { IwFsType, PathMarkSyncStatus } from "@/sdk/constants";

// Group marks by path
export interface PathMarkGroup {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  exists?: boolean; // undefined means not checked yet
}

/**
 * Hook to fetch and manage path marks
 */
export function usePathMarks() {
  const [allMarks, setAllMarks] = useState<BakabaseAbstractionsModelsDomainPathMark[]>([]);
  const [allPaths, setAllPaths] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [pathExistsMap, setPathExistsMap] = useState<Map<string, boolean>>(new Map());
  const [checkingPaths, setCheckingPaths] = useState(false);

  // Load all path marks on mount
  useEffect(() => {
    loadAllMarks();
  }, []);

  const loadAllMarks = useCallback(async () => {
    setLoading(true);
    try {
      const rsp = await BApi.pathMark.getAllPathMarks();
      if (!rsp.code && rsp.data) {
        setAllMarks(rsp.data);

        // Extract unique paths
        const paths = [...new Set(rsp.data.map((m) => m.path).filter(Boolean))] as string[];
        setAllPaths(paths);
      }
    } catch (error) {
      console.error("Failed to load path marks:", error);
    } finally {
      setLoading(false);
    }
  }, []);

  // Check if paths exist on file system
  const checkPathsExistence = useCallback(async () => {
    if (allPaths.length === 0) return;

    setCheckingPaths(true);
    const newPathExistsMap = new Map<string, boolean>();

    // Check all paths in parallel
    const results = await Promise.all(
      allPaths.map(async (path) => {
        try {
          const rsp = await BApi.file.getIwFsEntry({ path });
          // Path exists if we get a valid response with data and type is not Invalid
          const exists = !rsp.code && rsp.data != null && rsp.data.type !== IwFsType.Invalid;
          return { path, exists };
        } catch {
          return { path, exists: false };
        }
      })
    );

    for (const { path, exists } of results) {
      newPathExistsMap.set(path, exists);
    }

    setPathExistsMap(newPathExistsMap);
    setCheckingPaths(false);
  }, [allPaths]);

  // Check paths existence when allPaths changes
  useEffect(() => {
    if (allPaths.length > 0) {
      checkPathsExistence();
    }
  }, [allPaths, checkPathsExistence]);

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
    [allMarks]
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
    [allPaths, getMarksForPath]
  );

  /**
   * Check if a path has marks (exact match)
   */
  const hasMarks = useCallback(
    (path: string): boolean => {
      return getMarksForPath(path).length > 0;
    },
    [getMarksForPath]
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
    }));
  }, [allMarks, pathExistsMap]);

  /**
   * Get marks grouped by path, filtered by existence
   */
  const getGroupedMarksFiltered = useCallback((showOnlyInvalid: boolean): PathMarkGroup[] => {
    const groups = getGroupedMarks();
    if (!showOnlyInvalid) return groups;
    return groups.filter(group => group.exists === false);
  }, [getGroupedMarks]);

  return {
    allMarks,
    allPaths,
    loading,
    checkingPaths,
    pathExistsMap,
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
