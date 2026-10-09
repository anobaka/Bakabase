import type { ToastProps } from "@heroui/react";

/** Vertical dragging competes with reading long messages, especially on touch screens. */
export const toastPresentation: Partial<ToastProps> = {
  motionProps: { drag: false, dragListener: false },
  classNames: {
    content: "bakabase-toast-content",
    wrapper: "bakabase-toast-wrapper",
    title: "bakabase-toast-text",
    description: "bakabase-toast-text",
    progressTrack: "bakabase-toast-progress",
  },
};
