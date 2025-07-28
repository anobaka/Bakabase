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
      description={t<string>(
        "The interval (in seconds) after download completion or failure before restarting the task. Generally used for downloading content that updates periodically.",
      )}
      label={t("Check interval(s)")}
      value={interval}
      onValueChange={(value) => onChange(value)}
    />
  );
};

IntervalField.displayName = "IntervalField";

export default IntervalField;
