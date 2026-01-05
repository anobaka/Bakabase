"use client";

import { useTranslation } from "react-i18next";
import { CheckboxGroup } from "@heroui/react";

import { Checkbox } from "@/components/bakaui";

type Props = {
  isDuplicateAllowed?: boolean;
  onChange: (isDuplicateAllowed?: boolean) => void;
};
const AllowDuplicateField = ({ isDuplicateAllowed, onChange }: Props) => {
  const { t } = useTranslation();

  return (
    <CheckboxGroup
      description={t<string>("downloader.tip.allowDuplicateDesc")}
      label={t<string>("downloader.label.allowDuplicate")}
      orientation="horizontal"
      size="sm"
    >
      <Checkbox isSelected={isDuplicateAllowed} onValueChange={onChange}>
        {t("common.label.yes")}
      </Checkbox>
    </CheckboxGroup>
  );
};

AllowDuplicateField.displayName = "AllowDuplicateField";

export default AllowDuplicateField;
