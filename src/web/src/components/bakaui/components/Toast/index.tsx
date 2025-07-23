import { addToast, type ToastProps } from "@heroui/react";

type SimpleProps = { title: string; description?: string };

// 通用toast方法
function showToast(
  color: ToastProps["color"],
  titleOrProps: string | SimpleProps,
) {
  if (typeof titleOrProps === "string") {
    addToast({ color, title: titleOrProps });
  } else {
    addToast({ color, ...titleOrProps });
  }
}

const toast = {
  default: (titleOrProps: string | SimpleProps) =>
    showToast("default", titleOrProps),
  primary: (titleOrProps: string | SimpleProps) =>
    showToast("primary", titleOrProps),
  secondary: (titleOrProps: string | SimpleProps) =>
    showToast("secondary", titleOrProps),
  success: (titleOrProps: string | SimpleProps) =>
    showToast("success", titleOrProps),
  warning: (titleOrProps: string | SimpleProps) =>
    showToast("warning", titleOrProps),
  danger: (titleOrProps: string | SimpleProps) =>
    showToast("danger", titleOrProps),
  foreground: (titleOrProps: string | SimpleProps) =>
    showToast("foreground", titleOrProps),
};

export default toast;
