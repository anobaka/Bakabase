"use client";

import type { ComponentType } from "react";
import type { DestroyableProps } from "@/components/bakaui/types";

import { createContext, useContext } from "react";

export type CreatePortal = <P extends DestroyableProps>(
  C: ComponentType<P>,
  props: P,
) => { destroy: () => void; key: string };

export interface IContext {
  isDarkMode: boolean;
  createPortal: CreatePortal;
  isDebugging?: boolean;
}

const notInitializedCreatePortal: CreatePortal = () => {
  throw new Error("createPortal is not available outside BakabaseContextProvider");
};

const BakabaseContext = createContext<IContext>({
  createPortal: notInitializedCreatePortal,
  isDarkMode: false,
  isDebugging: false,
});

export const useBakabaseContext = (): IContext => {
  return useContext(BakabaseContext);
};

export default BakabaseContext;


