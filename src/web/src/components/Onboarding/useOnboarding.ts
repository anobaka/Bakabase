import { useState, useEffect, useCallback } from "react";

const ONBOARDING_KEY = "bakabase-onboarding-completed";

export const useOnboarding = () => {
  const [showOnboarding, setShowOnboarding] = useState(false);

  useEffect(() => {
    if (typeof window === "undefined" || typeof localStorage === "undefined") {
      return;
    }

    const completed = localStorage.getItem(ONBOARDING_KEY);
    if (!completed) {
      setShowOnboarding(true);
    }
  }, []);

  const completeOnboarding = useCallback(() => {
    if (typeof localStorage !== "undefined") {
      localStorage.setItem(ONBOARDING_KEY, "true");
    }
    setShowOnboarding(false);
  }, []);

  const resetOnboarding = useCallback(() => {
    if (typeof localStorage !== "undefined") {
      localStorage.removeItem(ONBOARDING_KEY);
    }
    setShowOnboarding(true);
  }, []);

  return {
    showOnboarding,
    completeOnboarding,
    resetOnboarding,
  };
};

export default useOnboarding;
