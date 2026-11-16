import { type ReactNode, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, Radio, RadioGroup, Tooltip } from "@/components/bakaui";
import { ResumeActionsContext } from "@/components/Workflow/ResumeActions";

export default function TaskContinuationModal({
  current, files, stale, origin, taskTitle, processAgain, delivery, busy = false, preferFiles = false, allowModeChoice = false, onClose,
}: {
  taskTitle?: string;
  origin?: ReactNode;
  processAgain?: boolean;
  delivery?: boolean;
  busy?: boolean;
  preferFiles?: boolean;
  allowModeChoice?: boolean;
  current?: ReactNode;
  files?: ReactNode;
  stale: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [actionContainer, setActionContainer] = useState<HTMLDivElement | null>(null);
  const [formBusy, setFormBusy] = useState(false);
  const [selectedMode, setSelectedMode] = useState<"current" | "files">();
  const isBusy = busy || formBusy;
  const canChoose = allowModeChoice && !!current && !!files;
  const mode = canChoose && selectedMode ? selectedMode : files && (preferFiles || !current) ? "files" : "current";
  const action = t(delivery ? "acquisition.continue.delivery" : processAgain ? "acquisition.continue.again" : "acquisition.continue.open");
  return (
    <Modal
      visible
      size="6xl"
      title={taskTitle ? `${taskTitle} · ${action}` : action}
      onClose={() => { if (!isBusy) onClose(); }}
      isDismissable={!isBusy}
      isKeyboardDismissDisabled={isBusy}
      hideCloseButton={isBusy}
      footer={<div className="flex w-full flex-wrap items-center justify-end gap-2">
        <Button variant="light" isDisabled={isBusy} onPress={onClose}>{t("acquisition.plan.close")}</Button>
        <div ref={setActionContainer} className="min-w-0" />
      </div>}
    >
      {origin}
      {canChoose && <div className="space-y-1">
        <RadioGroup label={t("acquisition.continue.checkpointChoice")} size="sm" orientation="horizontal"
          value={mode} isDisabled={isBusy || stale}
          onValueChange={value => { if (value === "current" || value === "files") setSelectedMode(value); }}>
          <Radio value="current">{t("acquisition.continue.checkpoint.current")}</Radio>
          <Radio value="files"><Tooltip content={t("acquisition.continue.checkpoint.filesHint")}>
            <span>{t("acquisition.continue.checkpoint.files")}</span>
          </Tooltip></Radio>
        </RadioGroup>
        {mode === "current" && <p className="text-xs text-default-500">{t("acquisition.continue.checkpoint.currentHint")}</p>}
      </div>}
      {stale && (
        <p role="alert" className="text-sm text-warning-700">
          {t("acquisition.plan.replan.error.PLAN_VERSION_CONFLICT")}
        </p>
      )}
      {mode === "current" && current && (
        <fieldset disabled={stale} className="min-w-0">
          <ResumeActionsContext.Provider value={{ container: actionContainer, disabled: stale, active: true, onBusyChange: setFormBusy }}>
            {current}
          </ResumeActionsContext.Provider>
        </fieldset>
      )}
      {mode === "files" && files && (
        <fieldset disabled={stale} className="min-w-0">
          <ResumeActionsContext.Provider value={{ container: actionContainer, disabled: stale, active: true, onBusyChange: setFormBusy }}>
            {files}
          </ResumeActionsContext.Provider>
        </fieldset>
      )}
    </Modal>
  );
}
