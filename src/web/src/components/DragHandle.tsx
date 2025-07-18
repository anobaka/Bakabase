"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import { MdDragIndicator } from "react-icons/md";

export default (props) => {
  const { style = {}, className, ...otherProps } = props || {};
  const { t } = useTranslation();

  return (
    <MdDragIndicator
      className={`drag-handle ${className || ""} cursor-pointer`}
      size={"small"}
      style={{ cursor: "all-scroll", ...style }}
      title={t<string>("Drag to sort")}
      {...(otherProps || {})}
    />
  );
};
