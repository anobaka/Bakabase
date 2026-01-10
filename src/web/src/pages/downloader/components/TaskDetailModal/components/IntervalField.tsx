"use client";

import { useTranslation } from "react-i18next";
import { NumberInput } from "@heroui/react";

type Props = {
  interval?: number;
  onChange: (interval?: number) => void;
};
const IntervalField = ({ interval, onChange }: Props) => {
  const { t } = useTranslation();

  return (
    <NumberInput
      description={t<string>("downloader.tip.checkIntervalDesc")}
      label={t("downloader.label.checkInterval")}
      value={interval}
      onValueChange={(value) => onChange(value)}
    />
  );
};

IntervalField.displayName = "IntervalField";

export default IntervalField;
