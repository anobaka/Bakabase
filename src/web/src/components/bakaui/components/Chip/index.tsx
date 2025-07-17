import type { ChipProps as NextUIChipProps } from "@heroui/react";

import { Chip as NextUIChip } from "@heroui/react";
import React, { forwardRef } from "react";

export interface ChipProps extends NextUIChipProps {
  children: React.ReactNode;
  size?: "sm" | "md" | "lg";
  className?: string;
  radius?: "full" | "sm" | "md" | "lg";
  isDisabled?: boolean;
  variant?:
    | "solid"
    | "bordered"
    | "light"
    | "flat"
    | "faded"
    | "shadow"
    | "dot";
  color?:
    | "default"
    | "primary"
    | "secondary"
    | "success"
    | "danger"
    | "warning";
}

const Chip = forwardRef<any, ChipProps>((props: ChipProps, ref) => {
  return <NextUIChip ref={ref} radius={"sm"} {...props} />;
});

export default Chip;
