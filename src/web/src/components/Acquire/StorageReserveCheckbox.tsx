import { useId } from "react";
import { useTranslation } from "react-i18next";
import { Checkbox, Tooltip } from "@/components/bakaui";

export const emergencyReserveBytes = 1024 ** 3;
export type StorageReserveMode = "inherit" | "emergency" | "configured";

/** The configured reserve stays distinct from the effective, scoped reserve. */
export default function StorageReserveCheckbox({
  scope, minimumBytes, selected, disabled, onChange,
}: {
  scope: "global" | "operation";
  minimumBytes: number;
  selected: boolean;
  disabled?: boolean;
  onChange: (selected: boolean) => void;
}) {
  const { t, i18n } = useTranslation();
  const descriptionId = useId();
  const minimum = new Intl.NumberFormat(i18n?.language, { maximumFractionDigits: 2 })
    .format(minimumBytes / emergencyReserveBytes) + " GiB";
  const label = t(`acquisition.reserve.${scope}.label`, { minimum });
  const hint = t(`acquisition.reserve.${scope}.hint`, { minimum });
  return (
    <div className={scope === "global" ? "inline-flex min-w-0 max-w-full items-center"
      : "inline-flex min-w-0 max-w-full items-center overflow-x-auto overscroll-x-contain py-1"}>
      <Tooltip content={hint} delay={750} placement="bottom"
        classNames={{ base: "pointer-events-none max-w-xs", content: "pointer-events-none" }}>
        <Checkbox color="warning" size="sm" aria-label={label} aria-describedby={descriptionId}
          classNames={scope === "global" ? {
            base: "m-0 min-w-0 max-w-full px-0", wrapper: "shrink-0 @max-[7rem]/acquisition:me-1", label: "min-w-0 truncate",
          } : {
            base: "m-0 w-max max-w-none flex-none flex-nowrap px-0", wrapper: "shrink-0", label: "whitespace-nowrap",
          }}
          isSelected={selected} isDisabled={disabled} onValueChange={onChange}>
          {label}
        </Checkbox>
      </Tooltip>
      <span id={descriptionId} className="sr-only">{hint}</span>
    </div>
  );
}
