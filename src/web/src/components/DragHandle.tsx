"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import CustomIcon from "@/components/CustomIcon";

export default (props) => {
  const { style = {}, className, ...otherProps } = props || {};
  const { t } = useTranslation();

  return (
    <CustomIcon
      className={`drag-handle ${className || ""} cursor-pointer`}
      size={"small"}
      style={{ cursor: "all-scroll", ...style }}
      title={t<string>("Drag to sort")}
      type={"menu"}
      {...(otherProps || {})}
    />
  );
};
