import { useTranslation } from "react-i18next";
import { Checkbox } from "@/components/bakaui";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";

export default function RetryDeliveryRootField({
  value,
  startPath,
  disabled,
  onChange,
}: {
  value?: string;
  startPath?: string | null;
  disabled: boolean;
  onChange: (path?: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Checkbox
        isSelected={value !== undefined}
        isDisabled={disabled}
        onValueChange={(enabled) => onChange(enabled ? "" : undefined)}
      >
        {t("acquisition.timeline.overrideDeliveryRoot")}
      </Checkbox>
      {value !== undefined && (
        <FileSystemSelectorButton
          wrapPath
          key={value}
          aria-label={t("acquisition.delivery.chooseRoot")}
          className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left"
          isDisabled={disabled}
          fileSystemSelectorProps={{
            targetType: "folder",
            multiple: false,
            startPath: value || startPath || undefined,
            defaultSelectedPath: value || undefined,
            onSelected: (entry) => {
              if (entry.path) onChange(entry.path);
            },
          }}
        >
          {t("acquisition.delivery.chooseRoot")}
        </FileSystemSelectorButton>
      )}
    </div>
  );
}
