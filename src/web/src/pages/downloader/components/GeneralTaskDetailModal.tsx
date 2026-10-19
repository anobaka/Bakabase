import type { DestroyableProps } from "@/components/bakaui/types";
import type { CenterTask, TaskAction } from "../downloadCenter";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import WorkflowRunDetailModal from "@/components/Workflow/WorkflowRunDetailModal";
import BApi from "@/sdk/BApi";
import { ResponseCode } from "@/sdk/constants";
import GeneralTaskRow from "./GeneralTaskRow";

export default function GeneralTaskDetailModal({
  task: initialTask,
  onDestroyed,
  onAction,
  onSource,
  onFolder,
}: DestroyableProps & {
  task: CenterTask;
  onAction: (id: string, action: TaskAction) => void;
  onSource: (id: number) => void;
  onFolder: (id: string) => void;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [task, setTask] = useState(initialTask);
  const [loadFailed, setLoadFailed] = useState(false);
  const [deleted, setDeleted] = useState(false);
  useEffect(() => {
    let active = true;
    let pending = false;
    const load = async () => {
      if (pending) return;
      pending = true;
      try {
        const response = await BApi.downloadCenter.getDownloadCenterTask(initialTask.id, {
          showErrorToast: false,
        });
        if (!active) return;
        if (response.code === ResponseCode.NotFound || (!response.code && !response.data)) {
          setDeleted(true);
          setLoadFailed(false);
          return;
        }
        if (response.code) throw new Error(response.message);
        setTask(response.data!);
        setDeleted(false);
        setLoadFailed(false);
      } catch {
        if (active) setLoadFailed(true);
      } finally {
        pending = false;
      }
    };
    setTask(initialTask);
    setDeleted(false);
    setLoadFailed(false);
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 3000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [initialTask.id]);
  return (
    <Modal
      defaultVisible
      size="3xl"
      title={t("downloadCenter.action.detail")}
      footer={false}
      onDestroyed={onDestroyed}
    >
      {deleted ? (
        <p role="status" className="text-sm text-default-500">
          {t("downloadCenter.detail.deleted")}
        </p>
      ) : (
        <>
          {loadFailed && (
            <p role="alert" className="text-sm text-danger">
              {t("downloadCenter.error.load")}
            </p>
          )}
          <GeneralTaskRow
            task={task}
            onSelect={() => {}}
            onAction={onAction}
            onSource={onSource}
            onFolder={onFolder}
          />
          <p className="break-all text-sm text-default-500">{task.outputDirectory}</p>
          {task.error && (
            <p role="alert" className="break-words text-sm text-danger">
              {task.error}
            </p>
          )}
          {!!task.files?.length && (
            <details className="text-sm text-default-500">
              <summary className="cursor-pointer">
                {t("bt.files", { count: task.files.length })}
              </summary>
              <ul className="mt-2 space-y-1 text-xs">
                {task.files.map((file) => (
                  <li key={file} className="break-all">
                    {file}
                  </li>
                ))}
              </ul>
            </details>
          )}
          {task.workflowRunId != null && (
            <Button
              variant="flat"
              onPress={() => createPortal(WorkflowRunDetailModal, { runId: task.workflowRunId! })}
            >
              {t("bt.action.workflow", { id: task.workflowRunId })}
            </Button>
          )}
        </>
      )}
      <p className="text-xs text-default-400">{t("downloadCenter.taskId", { id: task.id })}</p>
    </Modal>
  );
}
