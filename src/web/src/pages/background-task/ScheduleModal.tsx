import type { BTask } from "@/core/models/BTask";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import dayjs from "dayjs";

import {
  intervalInput,
  intervalUnits,
  intervalValue,
  mergeTaskOption,
  type IntervalUnit,
} from "./schedule";

import { Button, Input, Modal, Select, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

export default function ScheduleModal({ task, onClose }: { task: BTask; onClose: () => void }) {
  const { t } = useTranslation();
  const initial = intervalInput(task.interval);
  const [amount, setAmount] = useState(initial.amount);
  const [unit, setUnit] = useState<IntervalUnit>(initial.unit);
  // BTask dates are server-local wall times (the scheduler compares DateTime.Now).
  // Preserve that contract; do not convert a remote server's clock to the browser's zone.
  const [enableAfter, setEnableAfter] = useState(
    task.enableAfter ? dayjs(task.enableAfter).format("YYYY-MM-DDTHH:mm:ss") : "",
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const savingRef = useRef(false);

  const save = async () => {
    if (savingRef.current) return;
    const interval = intervalValue(amount, unit);

    if (!interval) {
      setError(t("backgroundTask.schedule.invalidInterval"));

      return;
    }
    if ((enableAfter && !dayjs(enableAfter).isValid()) || (!enableAfter && task.enableAfter)) {
      setError(t("backgroundTask.schedule.invalidEnableAfter"));

      return;
    }
    savingRef.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const current = await BApi.options.getTaskOptions({ showErrorToast: false });

      if (current.code || !current.data)
        throw new Error(current.message || t("backgroundTask.schedule.loadFailed"));
      const response = await BApi.options.patchTaskOptions(
        {
          tasks: mergeTaskOption(current.data.tasks, {
            id: task.id,
            interval,
            enableAfter: enableAfter || undefined,
          }),
        },
        { showErrorToast: false },
      );

      if (response.code)
        throw new Error(response.message || t("backgroundTask.schedule.saveFailed"));
      toast.success({ title: t("backgroundTask.schedule.saved") });
      onClose();
    } catch (failure) {
      setError(
        failure instanceof Error ? failure.message : t("backgroundTask.schedule.saveFailed"),
      );
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <Modal
      visible
      footer={
        <>
          <Button isDisabled={saving} variant="light" onPress={onClose}>
            {t("common.action.cancel")}
          </Button>
          <Button color="primary" isLoading={saving} onPress={() => void save()}>
            {t("common.action.save")}
          </Button>
        </>
      }
      hideCloseButton={saving}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      size="sm"
      title={t("backgroundTask.label.scheduleFor", { taskName: task.name })}
      onClose={onClose}
    >
      <p className="text-sm leading-relaxed text-default-500">
        {t("backgroundTask.schedule.description")}
      </p>
      <div className="grid grid-cols-[minmax(0,1fr)_8rem] gap-2">
        <Input
          isDisabled={saving}
          label={t("backgroundTask.column.interval")}
          min={0}
          size="sm"
          type="number"
          value={amount}
          onValueChange={setAmount}
        />
        <Select
          disallowEmptySelection
          dataSource={Object.keys(intervalUnits).map((value) => ({
            value,
            label: t(`common.unit.${value}`),
          }))}
          isDisabled={saving}
          label={t("backgroundTask.schedule.unit")}
          selectedKeys={[unit]}
          size="sm"
          onSelectionChange={(keys) => {
            if (keys !== "all") {
              const value = Array.from(keys)[0] as IntervalUnit;

              if (value in intervalUnits) setUnit(value);
            }
          }}
        />
      </div>
      <Input
        description={t("backgroundTask.schedule.serverTime")}
        isDisabled={saving}
        label={t("backgroundTask.schedule.enableAfter")}
        size="sm"
        step="1"
        type="datetime-local"
        value={enableAfter}
        onValueChange={setEnableAfter}
      />
      {error && (
        <p className="break-words text-sm text-danger" role="alert">
          {error}
        </p>
      )}
    </Modal>
  );
}
