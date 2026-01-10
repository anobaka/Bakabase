"use client";

import { useTranslation } from "react-i18next";

type Props = {
  size?: "sm" | "md" | "lg";
};

const NoChoicesAvailable = ({ size }: Props) => {
  const { t } = useTranslation();

  return (
    <span className="text-default-400 text-sm">
      {t("common.state.noChoicesAvailable")}
    </span>
  );
};

NoChoicesAvailable.displayName = "NoChoicesAvailable";

export default NoChoicesAvailable;
