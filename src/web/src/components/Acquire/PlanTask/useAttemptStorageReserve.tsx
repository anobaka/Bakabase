import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Spinner } from "@/components/bakaui";
import StorageReserveCheckbox from "../StorageReserveCheckbox";
import useStorageReserveDefaults from "../useStorageReserveDefaults";

export type AttemptRunOptions = { useEmergencySpaceReserve: boolean };

/** Capture the displayed choice for this run; never rewrite the global default. */
export default function useAttemptStorageReserve(disabled: boolean) {
  const { t } = useTranslation();
  const defaults = useStorageReserveDefaults(true);
  const [choice, setChoice] = useState<boolean>();
  const selected = choice ?? defaults.value?.selected;
  return {
    ready: !!defaults.value && typeof selected === "boolean",
    options: selected === undefined ? undefined : { useEmergencySpaceReserve: selected },
    control: <div className="min-w-0 max-w-full">
      {defaults.value && <StorageReserveCheckbox scope="operation"
        minimumBytes={defaults.value.minimumBytes} selected={selected === true}
        disabled={disabled} onChange={setChoice} />}
      {!defaults.value && !defaults.failed && <Spinner size="sm" aria-label={t("acquisition.plan.loading")} />}
      {defaults.failed && <Button size="sm" variant="light" color="warning"
        isDisabled={disabled} onPress={defaults.reload}>{t("acquisition.reserve.reload")}</Button>}
    </div>,
  };
}
