import { useCallback, useEffect, useState } from "react";
import { acquisitionTaskTitle } from "@/components/Acquire/acquisitionTaskTitle";
import { useTranslation } from "react-i18next";
import { Button, Modal, Spinner } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import WorkflowRunDetailModal from "@/components/Workflow/WorkflowRunDetailModal";
import SourceExecutionHistory from "@/components/Acquire/SourceExecutionHistory";
import AcquisitionRow from "./AcquisitionRow";
import DeleteAcquisitionTasksModal from "./DeleteAcquisitionTasksModal";
import type { AcquisitionRecipeVm, AcquisitionTaskVm } from "..";

export default function LegacyTaskDetailModal({
  taskId,
  recipes,
  onChanged,
  onEditRecipe,
  onDestroyed,
}: DestroyableProps & {
  taskId: number;
  recipes: AcquisitionRecipeVm[];
  onChanged: () => void;
  onEditRecipe: (id: number) => void;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [visible, setVisible] = useState(true);
  const [task, setTask] = useState<AcquisitionTaskVm>();
  const [failed, setFailed] = useState(false);
  const [history, setHistory] = useState(false);
  const load = useCallback(async () => {
    try {
      const response = await BApi.acquisition.getAcquisition(taskId);
      if (response.code || !response.data) throw new Error();
      setTask(response.data);
      setFailed(false);
    } catch {
      setFailed(true);
    }
  }, [taskId]);
  useEffect(() => {
    void load();
  }, [load]);
  const readOnly = !!(task?.isDeleted || task?.deletedAt);
  return (
    <>
      <Modal
        visible={visible}
        onClose={() => setVisible(false)}
        footer={<>
          {task && !readOnly && <Button color="danger" variant="light" onPress={() => {
            setVisible(false);
            createPortal(DeleteAcquisitionTasksModal, { taskIds: [taskId], taskNames: { [taskId]: task.title?.trim() || task.resourceName?.trim() || "" }, onChanged });
          }}>{t("acquisition.delete.single")}</Button>}
          <Button variant="light" onPress={() => setVisible(false)}>{t("common.action.close")}</Button>
        </>}
        size="5xl"
        classNames={{ header: "min-w-0 pr-10 [overflow-wrap:anywhere]" }}
        title={acquisitionTaskTitle(taskId, task)}
        onDestroyed={onDestroyed}
      >
        {failed && <p role="alert">{t("acquisition.plan.loadFailed")}</p>}
        {!task ? (
          !failed && <Spinner />
        ) : (
          <div className="space-y-3">
            {readOnly && (
              <p role="status" className="rounded-lg bg-default-100 p-3 text-sm text-default-600">
                {t("acquisition.delete.readOnly")}
              </p>
            )}
            <AcquisitionRow
              inDetail
              task={task}
              recipes={recipes}
              onChanged={() => {
                void load();
                onChanged();
              }}
              onEditRecipe={onEditRecipe}
              onOriginNavigate={() => setVisible(false)}
            />
            <div className="flex flex-wrap gap-2">
              {task.workflowRunId && (
                <Button
                  size="sm"
                  variant="flat"
                  onPress={() =>
                    createPortal(WorkflowRunDetailModal, { runId: task.workflowRunId!, readOnly })
                  }
                >
                  {t("acquisition.plan.execution")}
                </Button>
              )}
              <Button size="sm" variant="flat" onPress={() => setHistory(true)}>
                {t("acquire.history.title")}
              </Button>
            </div>
          </div>
        )}
      </Modal>
      {history && (
        <SourceExecutionHistory
          readOnly={readOnly}
          source={{ module: "acquisition", entityKind: "task", entityId: String(taskId) }}
          onClose={() => setHistory(false)}
        />
      )}
    </>
  );
}
