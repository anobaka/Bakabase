import { useState, useEffect, useCallback } from "react";

const STORAGE_KEY = "bakabase-filter-options-threshold";
export const DEFAULT_FILTER_OPTIONS_THRESHOLD = 30;

/**
 * Get the filter options threshold from localStorage
 * This is a static function that can be called outside of React components
 */
export const getFilterOptionsThreshold = (): number => {
  if (typeof window === "undefined" || typeof localStorage === "undefined") {
    return DEFAULT_FILTER_OPTIONS_THRESHOLD;
  }

  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      const value = parseInt(stored, 10);
      if (!isNaN(value) && value > 0) {
        return value;
      }
    }
  } catch (error) {
    console.warn("Failed to read filter options threshold from localStorage:", error);
  }

  return DEFAULT_FILTER_OPTIONS_THRESHOLD;
};

/**
 * Set the filter options threshold to localStorage
 */
export const setFilterOptionsThreshold = (value: number): void => {
  if (typeof window === "undefined" || typeof localStorage === "undefined") {
    return;
  }

  try {
    localStorage.setItem(STORAGE_KEY, value.toString());
    // Dispatch a custom event so other components can react to the change
    window.dispatchEvent(new CustomEvent("filter-options-threshold-change", { detail: value }));
  } catch (error) {
    console.warn("Failed to save filter options threshold to localStorage:", error);
  }
};

/**
 * Hook to manage the filter options threshold setting.
 * This threshold determines how many options are shown before collapsing
 * in choice, tags, and multilevel property selectors.
 *
 * @returns [threshold, setThreshold] - Current threshold value and setter function
 *
 * @example
 * const [threshold, setThreshold] = useFilterOptionsThreshold();
 * // threshold defaults to 30
 * // Use setThreshold(50) to change it
 */
export const useFilterOptionsThreshold = (): [number, (value: number) => void] => {
  const [threshold, setThresholdState] = useState<number>(getFilterOptionsThreshold);

  // Listen for changes from other components
  useEffect(() => {
    const handleChange = (event: CustomEvent<number>) => {
      setThresholdState(event.detail);
    };

    window.addEventListener("filter-options-threshold-change", handleChange as EventListener);
    return () => {
      window.removeEventListener("filter-options-threshold-change", handleChange as EventListener);
    };
  }, []);

  const setThreshold = useCallback((value: number) => {
    if (value > 0) {
      setThresholdState(value);
      setFilterOptionsThreshold(value);
    }
  }, []);

  return [threshold, setThreshold];
};

export default useFilterOptionsThreshold;
