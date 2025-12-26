"use client";

import React from "react";
import { useTranslation } from "react-i18next";

export const EnhancerDescription = () => {
  const { t } = useTranslation();

  return (
    <div className="space-y-2">
      <p>{t("Term.Enhancer.Description")}</p>
    </div>
  );
};

EnhancerDescription.displayName = "EnhancerDescription";
