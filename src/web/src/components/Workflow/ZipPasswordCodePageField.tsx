import { useTranslation } from "react-i18next";
import { Select } from "@/components/bakaui";
import { zipPasswordCodePages, type ZipPasswordCodePage } from "@/sdk/constants";
import { isZipPasswordCodePage } from "./zipPasswordCodePage";

export default function ZipPasswordCodePageField({ value, onChange, isDisabled }: {
  value?: ZipPasswordCodePage | null;
  onChange: (value: ZipPasswordCodePage | undefined) => void;
  isDisabled?: boolean;
}) {
  const { t } = useTranslation();
  return <Select
    size="sm"
    label={t("workflow.processing.zipPasswordCodePage.label")}
    description={t("workflow.processing.zipPasswordCodePage.hint")}
    isDisabled={isDisabled}
    selectedKeys={[value == null ? "auto" : String(value)]}
    dataSource={[
      { value: "auto", label: t("workflow.processing.zipPasswordCodePage.auto") },
      ...zipPasswordCodePages.map((choice) => ({
        value: String(choice.value),
        label: t(`workflow.processing.zipPasswordCodePage.${choice.value}`),
      })),
    ]}
    onSelectionChange={(keys) => {
      const selected = Array.from(keys)[0];
      if (selected === "auto") onChange(undefined);
      else if (selected != null && isZipPasswordCodePage(Number(selected)))
        onChange(Number(selected) as ZipPasswordCodePage);
    }}
  />;
}
