"use client";

import { useTranslation } from "react-i18next";
import { CheckboxGroup } from "@heroui/react";

import { Checkbox } from "@/components/bakaui";

type Props = {
  autoRetry?: boolean;
  onChange: (autoRetry?: boolean) => void;
};
const AutoRetryField = ({ autoRetry, onChange }: Props) => {
  const { t } = useTranslation();

  return (
    <CheckboxGroup
      description={t<string>("downloader.tip.autoRetryDesc")}
      label={t<string>("downloader.label.autoRetry")}
      orientation="horizontal"
      size="sm"
    >
      <Checkbox defaultSelected isSelected={autoRetry} onValueChange={onChange}>
        {t("common.label.yes")}
      </Checkbox>
    </CheckboxGroup>
  );
};

AutoRetryField.displayName = "AutoRetryField";

export default AutoRetryField;
