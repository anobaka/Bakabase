import { create } from "zustand";

export type ClipboardMode = "copy" | "cut";

interface FileExplorerClipboardState {
  paths: string[];
  mode: ClipboardMode;

  // Actions
  copy: (paths: string[]) => void;
  cut: (paths: string[]) => void;
  clear: () => void;
  hasPaths: () => boolean;
  isInClipboard: (path: string) => boolean;
}

export const useFileExplorerClipboardStore = create<FileExplorerClipboardState>((set, get) => ({
  paths: [],
  mode: "copy",

  copy: (paths) =>
    set({
      paths,
      mode: "copy",
    }),

  cut: (paths) =>
    set({
      paths,
      mode: "cut",
    }),

  clear: () =>
    set({
      paths: [],
    }),

  hasPaths: () => get().paths.length > 0,

  isInClipboard: (path) => get().paths.includes(path),
}));
