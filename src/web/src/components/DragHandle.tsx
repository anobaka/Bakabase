"use client";

import type { ChipProps } from "@heroui/react";

import { useTranslation } from "react-i18next";
import { Chip } from "@heroui/react";
import { AiOutlineDrag } from "react-icons/ai";
const DragHandle = (props: ChipProps) => {
  const { style = {}, className, ...otherProps } = props || {};
  const { t } = useTranslation();

  return (
    <Chip
      className={`drag-handle ${className || ""} cursor-pointer`}
      size={"sm"}
      style={{ cursor: "all-scroll", ...style }}
      title={t<string>("Drag to sort")}
      variant={"light"}
      {...(otherProps || {})}
    >
      <AiOutlineDrag className={"text-lg"} />
    </Chip>
  );
};

DragHandle.displayName = "DragHandle";

export default DragHandle;
