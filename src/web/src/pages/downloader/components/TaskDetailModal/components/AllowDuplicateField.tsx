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
      description={t<string>(
        "In general, it's not necessary to create identical tasks.",
      )}
      label={t<string>("Allow duplicate submission")}
      orientation="horizontal"
      size="sm"
    >
      <Checkbox isSelected={isDuplicateAllowed} onValueChange={onChange}>
        {t("Yes")}
      </Checkbox>
    </CheckboxGroup>
  );
};

AllowDuplicateField.displayName = "AllowDuplicateField";

export default AllowDuplicateField;
