import { useTranslation } from "react-i18next";
import { Select } from "@/components/bakaui";
import { archiveOpenModes, type ArchiveOpenMode } from "@/sdk/constants";
import { isArchiveOpenMode } from "./archiveOpenMode";

export default function ArchiveOpenModeField({
  value,
  onChange,
  isDisabled,
}: {
  value?: ArchiveOpenMode | null;
  onChange: (value: ArchiveOpenMode | undefined) => void;
  isDisabled?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <Select
      size="sm"
      label={t("workflow.processing.archiveOpenMode.label")}
      description={t("workflow.processing.archiveOpenMode.hint")}
      isDisabled={isDisabled}
      selectedKeys={[value == null ? "auto" : String(value)]}
      dataSource={[
        { value: "auto", label: t("workflow.processing.archiveOpenMode.auto") },
        ...archiveOpenModes.map((choice) => ({
          value: String(choice.value),
          label: t(`workflow.processing.archiveOpenMode.${choice.value}`),
        })),
      ]}
      onSelectionChange={(keys) => {
        const selected = Array.from(keys)[0];
        if (selected === "auto") onChange(undefined);
        else if (selected != null && isArchiveOpenMode(Number(selected)))
          onChange(Number(selected) as ArchiveOpenMode);
      }}
    />
  );
}
