"use client";

import { forwardRef } from "react";
import type { ButtonProps } from "@/components/bakaui";

import { Button } from "@/components/bakaui";

type Props = {} & Omit<ButtonProps, "ref">;
const OperationButton = forwardRef<HTMLButtonElement, Props>(({
  onClick,
  children,
  size = "sm",
  variant = "light",
  className,
  ...props
}, ref) => {
  return (
    <Button
      ref={ref}
      className={`w-auto h-auto p-1 min-w-fit opacity-70 hover:opacity-100 ${className ?? ""}`}
      size={size}
      variant={variant}
      onClick={(e) => {
        e.stopPropagation();
        onClick?.(e);
      }}
      {...props}
    >
      {children}
    </Button>
  );
});

OperationButton.displayName = "OperationButton";

export default OperationButton;
