import { useTranslation } from "react-i18next";
import { Select } from "@/components/bakaui";
import { zipFileNameCodePages, type ZipFileNameCodePage } from "@/sdk/constants";

import { isZipFileNameCodePage } from "./zipFileNameCodePage";

export default function ZipFileNameCodePageField({
  value,
  onChange,
  isDisabled,
}: {
  value?: ZipFileNameCodePage | null;
  onChange: (value: ZipFileNameCodePage | undefined) => void;
  isDisabled?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <Select
      size="sm"
      label={t("workflow.processing.zipFileNameCodePage.label")}
      description={t("workflow.processing.zipFileNameCodePage.hint")}
      isDisabled={isDisabled}
      selectedKeys={[value == null ? "auto" : String(value)]}
      dataSource={[
        { value: "auto", label: t("workflow.processing.zipFileNameCodePage.auto") },
        ...zipFileNameCodePages.map((choice) => ({
          value: String(choice.value),
          label: t(`workflow.processing.zipFileNameCodePage.${choice.value}`),
        })),
      ]}
      onSelectionChange={(keys) => {
        const selected = Array.from(keys)[0];
        if (selected === "auto") onChange(undefined);
        else if (selected != null) {
          const codePage = Number(selected);
          if (isZipFileNameCodePage(codePage)) onChange(codePage);
        }
      }}
    />
  );
}
