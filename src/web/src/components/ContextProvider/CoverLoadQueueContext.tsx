"use client";

import type { ReactNode } from "react";

import { createContext, useContext, useRef } from "react";

import { CoverLoadQueue } from "@/utils/CoverLoadQueue";

const CoverLoadQueueContext = createContext<CoverLoadQueue | null>(null);

/**
 * Hook to access the global CoverLoadQueue instance.
 * Use this to ensure concurrent cover loading is controlled across all components.
 */
export const useCoverLoadQueue = (): CoverLoadQueue => {
  const queue = useContext(CoverLoadQueueContext);
  if (!queue) {
    throw new Error("useCoverLoadQueue must be used within CoverLoadQueueProvider");
  }
  return queue;
};

interface CoverLoadQueueProviderProps {
  children: ReactNode;
  concurrency?: number;
}

/**
 * Provider component that creates and manages a global CoverLoadQueue instance.
 * Wrap your app with this provider to enable controlled concurrent cover loading.
 */
export const CoverLoadQueueProvider = ({
  children,
  concurrency = 6,
}: CoverLoadQueueProviderProps) => {
  const queueRef = useRef<CoverLoadQueue | null>(null);

  if (!queueRef.current) {
    queueRef.current = new CoverLoadQueue(concurrency);
  }

  return (
    <CoverLoadQueueContext.Provider value={queueRef.current}>
      {children}
    </CoverLoadQueueContext.Provider>
  );
};

export default CoverLoadQueueContext;
