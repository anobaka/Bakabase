"use client";

import React from "react";

import { Chip, Popover } from "@/components/bakaui";

type Props = {
  className?: string;
  size?: "sm" | "md" | "lg";
  variant?: "solid" | "bordered" | "light" | "flat" | "faded" | "shadow";
  color?:
    | "default"
    | "primary"
    | "secondary"
    | "success"
    | "warning"
    | "danger";
  label: string;
  description: React.ReactNode;
};

const TermChip = ({
  className,
  size = "sm",
  variant = "light",
  color = "success",
  label,
  description,
}: Props) => {
  const chip = (
    <Chip
      className="cursor-pointer !px-0 !py-0 !h-auto !min-h-0"
      color={color}
      size={size}
      variant={variant}
      classNames={{
        content: "!px-0",
      }}
    >
      {label}
    </Chip>
  );

  return (
    <Popover
      placement="bottom"
      trigger={chip}
      classNames={{
        trigger: `inline-flex items-center ${className || ""}`,
      }}
    >
      <div className="p-3 max-w-xs">
        {description}
      </div>
    </Popover>
  );
};

TermChip.displayName = "TermChip";

export default TermChip;
