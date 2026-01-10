import { useState, useEffect, useCallback } from "react";

const GUIDE_KEY = "bakabase-path-mark-guide-completed";

export const usePathMarkGuide = () => {
  const [showGuide, setShowGuide] = useState(false);

  useEffect(() => {
    if (typeof window === "undefined" || typeof localStorage === "undefined") {
      return;
    }

    const completed = localStorage.getItem(GUIDE_KEY);
    if (!completed) {
      setShowGuide(true);
    }
  }, []);

  const completeGuide = useCallback(() => {
    if (typeof localStorage !== "undefined") {
      localStorage.setItem(GUIDE_KEY, "true");
    }
    setShowGuide(false);
  }, []);

  const resetGuide = useCallback(() => {
    if (typeof localStorage !== "undefined") {
      localStorage.removeItem(GUIDE_KEY);
    }
    setShowGuide(true);
  }, []);

  return {
    showGuide,
    completeGuide,
    resetGuide,
  };
};

export default usePathMarkGuide;
