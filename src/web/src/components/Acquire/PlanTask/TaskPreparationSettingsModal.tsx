import { useTranslation } from "react-i18next";
import { Button, Modal } from "@/components/bakaui";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import TaskStoragePolicySettings from "./TaskStoragePolicySettings";
import TaskDeliverySettings from "./TaskDeliverySettings";
import type { PlanTaskDetail } from "./models";
import { planTaskApi } from "./api";

export default function TaskPreparationSettingsModal({ detail, busy, onAction, onClose }: {
  detail: PlanTaskDetail;
  busy: boolean;
  onAction: (action: () => Promise<PlanTaskDetail>) => Promise<void>;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const task = detail.task;
  return <Modal visible size="4xl" scrollBehavior="inside" title={t("acquisition.preparation.settings")}
    onClose={onClose} footer={<Button variant="light" onPress={onClose}>{t("common.action.close")}</Button>}>
    <div className="space-y-4">
      <section className="space-y-2">
        <h3 className="text-sm font-medium">{t("acquisition.plan.outputDirectory")}</h3>
        <p className="text-xs text-default-500">{t("acquisition.plan.outputHint")}</p>
        <FileSystemSelectorButton key={`${task.targetDirectory}:${task.planRevision}`} isDisabled={busy}
          className="h-auto max-w-full whitespace-normal break-all py-2 text-left"
          fileSystemSelectorProps={{ targetType: "folder", defaultSelectedPath: task.targetDirectory,
            onSelected: entry => void onAction(() => task.builderId === "draft"
              ? planTaskApi.updateDraft(task.id, { expectedRevision: task.planRevision, outputDirectory: entry.path })
              : planTaskApi.update(task.id, task.planRevision, entry.path)) }} />
      </section>
      {detail.canEditStoragePolicy && <TaskStoragePolicySettings detail={detail} busy={busy}
        onSave={async (revision, policy, overrides) => {
          let saved = false;
          await onAction(async () => {
            const next = await planTaskApi.updateStorage(task.id, revision, policy, overrides.map(item => ({ ...item, policy: item.policy ?? undefined })));
            saved = true;
            return next;
          });
          return saved;
        }} />}
      {detail.canEditDeliverySettings && <TaskDeliverySettings key={`${task.id}:${task.planRevision}`} detail={detail} busy={busy} editable
        onSave={settings => onAction(() => task.builderId === "draft"
          ? planTaskApi.updateDraft(task.id, { expectedRevision: task.planRevision, deliverySettings: settings })
          : planTaskApi.updateDelivery(task.id, task.planRevision, settings))} />}
    </div>
  </Modal>;
}
