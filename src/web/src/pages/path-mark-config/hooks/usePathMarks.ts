import { useCallback, useEffect, useState } from "react";
import BApi from "@/sdk/BApi";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import { PathMarkSyncStatus } from "@/sdk/constants";

// Group marks by path
export interface PathMarkGroup {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
}

/**
 * Hook to fetch and manage path marks
 */
export function usePathMarks() {
  const [allMarks, setAllMarks] = useState<BakabaseAbstractionsModelsDomainPathMark[]>([]);
  const [allPaths, setAllPaths] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);

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
    }));
  }, [allMarks]);

  return {
    allMarks,
    allPaths,
    loading,
    loadAllMarks,
    getMarksForPath,
    getApplicableMarksGroup,
    hasMarks,
    getGroupedMarks,
  };
}

export default usePathMarks;
