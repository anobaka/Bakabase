import { useTranslation } from "react-i18next";
import { Select } from "@/components/bakaui";

import { isExtractionLayout, type ExtractionLayout } from "./extractionLayout";

export default function ExtractionLayoutField({
  value,
  onChange,
  step = false,
  isDisabled,
}: {
  value?: ExtractionLayout | null;
  onChange: (value: ExtractionLayout | undefined) => void;
  step?: boolean;
  isDisabled?: boolean;
}) {
  const { t } = useTranslation();
  const unset = step ? "inherit" : "legacy";
  const choices = ["currentDirectory", "archiveDirectory"];
  if (step || value == null) choices.unshift(unset);
  return (
    <Select
      size="sm"
      label={t("workflow.extraction.layout.label")}
      description={t("workflow.extraction.layout.hint")}
      isDisabled={isDisabled}
      selectedKeys={[value ?? unset]}
      dataSource={choices.map((key) => ({
        value: key,
        label: t(`workflow.extraction.layout.${key}`),
      }))}
      onSelectionChange={(keys) => {
        const selected = Array.from(keys)[0];
        if (isExtractionLayout(selected)) onChange(selected);
        else if (selected === unset) onChange(undefined);
      }}
    />
  );
}
