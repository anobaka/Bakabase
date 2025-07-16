import { addToast, type ToastProps } from "@heroui/react";

// 通用toast方法
function showToast(
  color: ToastProps["color"],
  titleOrProps: string | ToastProps,
) {
  if (typeof titleOrProps === "string") {
    addToast({ color, title: titleOrProps });
  } else {
    addToast({ color, ...titleOrProps });
  }
}

const toast = {
  default: (titleOrProps: string | ToastProps) =>
    showToast("default", titleOrProps),
  primary: (titleOrProps: string | ToastProps) =>
    showToast("primary", titleOrProps),
  secondary: (titleOrProps: string | ToastProps) =>
    showToast("secondary", titleOrProps),
  success: (titleOrProps: string | ToastProps) =>
    showToast("success", titleOrProps),
  warning: (titleOrProps: string | ToastProps) =>
    showToast("warning", titleOrProps),
  danger: (titleOrProps: string | ToastProps) =>
    showToast("danger", titleOrProps),
  foreground: (titleOrProps: string | ToastProps) =>
    showToast("foreground", titleOrProps),
};

export default toast;
