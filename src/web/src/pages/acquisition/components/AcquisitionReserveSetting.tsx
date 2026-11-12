import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import BApi from "@/sdk/BApi";
import { Button, Spinner } from "@/components/bakaui";
import StorageReserveCheckbox, { emergencyReserveBytes } from "@/components/Acquire/StorageReserveCheckbox";

const requestOptions = { showErrorToast: false };
type Setting = { selected: boolean; minimumBytes: number };

export default function AcquisitionReserveSetting({ revision }: { revision: number }) {
  const { t } = useTranslation();
  const [setting, setSetting] = useState<Setting>();
  const [failed, setFailed] = useState(false);
  const [saving, setSaving] = useState(false);
  const active = useRef(true);
  const write = useRef(false);
  const sequence = useRef(0);
  const load = useCallback(async () => {
    const seq = ++sequence.current;
    try {
      const result = await BApi.acquisition.getAcquisitionOptions(requestOptions);
      const next = result.data;
      if (result.code || typeof next?.useEmergencySpaceReserve !== "boolean" ||
          typeof next.minimumFreeSpaceBytes !== "number") throw new Error("ACQUISITION_OPTIONS_UNAVAILABLE");
      if (active.current && seq === sequence.current) {
        setSetting({ selected: next.useEmergencySpaceReserve, minimumBytes: next.minimumFreeSpaceBytes });
        setFailed(false);
      }
    } catch {
      if (active.current && seq === sequence.current) setFailed(true);
    }
  }, []);
  useEffect(() => {
    active.current = true;
    void load();
    return () => { active.current = false; ++sequence.current; };
  }, [load, revision]);
  const save = async (selected: boolean) => {
    if (write.current || !setting || failed || selected === setting.selected) return;
    write.current = true;
    ++sequence.current;
    setSaving(true);
    try {
      const result = await BApi.acquisition.setUpAcquisition({ useEmergencySpaceReserve: selected }, requestOptions);
      if (result.code) throw new Error("ACQUISITION_OPTIONS_SAVE_FAILED");
      await load();
    } catch {
      if (active.current) setFailed(true);
    } finally {
      write.current = false;
      if (active.current) setSaving(false);
    }
  };
  return (
    <div className="flex w-fit max-w-full flex-none items-center gap-1">
      {setting && (
        <StorageReserveCheckbox scope="global" minimumBytes={setting.minimumBytes} selected={setting.selected}
          disabled={saving || failed || (setting.minimumBytes <= emergencyReserveBytes && !setting.selected)}
          onChange={(selected) => void save(selected)} />
      )}
      {(!setting && !failed || saving) && <Spinner size="sm" aria-label={t("acquisition.plan.loading")} />}
      {failed && (
        <Button variant="light" color="warning" size="sm" onPress={() => void load()}>
          {t("acquisition.reserve.reload")}
        </Button>
      )}
    </div>
  );
}
