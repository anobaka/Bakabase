"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import { Chip, Tooltip } from "@/components/bakaui";
const AliasAppliedChip = () => {
  const { t } = useTranslation();

  return (
    <Tooltip
      color={"foreground"}
      content={t<string>("You can search values by aliases also")}
    >
      <Chip size={"sm"}>{t<string>("Alias applied")}</Chip>
    </Tooltip>
  );
};

AliasAppliedChip.displayName = "AliasAppliedChip";

export default AliasAppliedChip;
