import { useCallback, useEffect, useState } from "react";
import BApi from "@/sdk/BApi";
import type { BakabaseAbstractionsModelsDomainPathRule } from "@/sdk/Api";

export type PathRuleMap = Record<string, BakabaseAbstractionsModelsDomainPathRule | null>;

/**
 * Hook to fetch and manage path rules
 */
export function usePathRules() {
  const [allRules, setAllRules] = useState<BakabaseAbstractionsModelsDomainPathRule[]>([]);
  const [loading, setLoading] = useState(false);

  // Load all path rules on mount
  useEffect(() => {
    loadAllRules();
  }, []);

  const loadAllRules = useCallback(async () => {
    setLoading(true);
    try {
      const rsp = await BApi.pathRule.getAllPathRules();
      if (!rsp.code && rsp.data) {
        setAllRules(rsp.data);
      }
    } catch (error) {
      console.error("Failed to load path rules:", error);
    } finally {
      setLoading(false);
    }
  }, []);

  /**
   * Get the rule that applies to a specific path (if any)
   * A rule applies if the path starts with the rule's path
   */
  const getRuleForPath = useCallback(
    (path: string): BakabaseAbstractionsModelsDomainPathRule | null => {
      if (!path) return null;

      const normalizedPath = path.replace(/\\/g, "/").toLowerCase();

      // Find the most specific rule (longest matching path)
      let bestMatch: BakabaseAbstractionsModelsDomainPathRule | null = null;
      let bestMatchLength = 0;

      for (const rule of allRules) {
        const rulePath = rule.path.replace(/\\/g, "/").toLowerCase();
        if (
          normalizedPath.startsWith(rulePath) ||
          normalizedPath === rulePath
        ) {
          if (rulePath.length > bestMatchLength) {
            bestMatch = rule;
            bestMatchLength = rulePath.length;
          }
        }
      }

      return bestMatch;
    },
    [allRules]
  );

  /**
   * Check if a path has an exact rule match (not inherited)
   */
  const hasExactRule = useCallback(
    (path: string): boolean => {
      if (!path) return false;
      const normalizedPath = path.replace(/\\/g, "/").toLowerCase();
      return allRules.some(
        (rule) => rule.path.replace(/\\/g, "/").toLowerCase() === normalizedPath
      );
    },
    [allRules]
  );

  /**
   * Get the exact rule for a path (only if the path has its own rule)
   */
  const getExactRule = useCallback(
    (path: string): BakabaseAbstractionsModelsDomainPathRule | null => {
      if (!path) return null;
      const normalizedPath = path.replace(/\\/g, "/").toLowerCase();
      return (
        allRules.find(
          (rule) =>
            rule.path.replace(/\\/g, "/").toLowerCase() === normalizedPath
        ) ?? null
      );
    },
    [allRules]
  );

  return {
    allRules,
    loading,
    loadAllRules,
    getRuleForPath,
    hasExactRule,
    getExactRule,
  };
}

export default usePathRules;
