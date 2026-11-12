import { lazy, Suspense } from "react";
import { useTranslation } from "react-i18next";
import { Button, Spinner } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionTask as AcquisitionTask } from "@/sdk/Api";
import { acquisitionStatusLabel } from "@/components/Workflow/valueLabels";
import { AcquisitionStatus } from "@/sdk/constants";

const Detail = lazy(() => import("./PlanTask/PlanTaskDetailModal"));
export default function SourceAcquisitionSummary({
  tasks = [],
  onChanged,
}: {
  tasks?: AcquisitionTask[];
  onChanged: () => void;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  if (!tasks.length) return null;
  return (
    <div className="space-y-1 rounded-lg border border-primary-100 p-2">
      <p className="text-xs font-medium">{t("acquire.plan.related", { count: tasks.length })}</p>
      <div className="flex flex-wrap gap-2">
        {tasks.map((task) => (
          <Button
            key={task.id}
            size="sm"
            variant="flat"
            color="primary"
            onPress={() =>
              createPortal(
                ({ onDestroyed }: DestroyableProps) => (
                  <Suspense fallback={<Spinner />}>
                    <Detail taskId={task.id} onDestroyed={onDestroyed} onChanged={onChanged} />
                  </Suspense>
                ),
                {},
              )
            }
          >
            {t("acquire.plan.taskPhase", {
              id: task.id,
              phase:
                task.isDeleted || task.deletedAt
                  ? t("acquisition.delete.deletedHistory")
                  : [
                        AcquisitionStatus.Failed,
                        AcquisitionStatus.Cancelled,
                        AcquisitionStatus.Completed,
                      ].includes(task.status)
                    ? acquisitionStatusLabel(t, task.status)
                    : `${acquisitionStatusLabel(t, task.status)} · ${t(`acquisition.plan.phase.${task.phase ?? "preparing"}`)}`,
            })}
          </Button>
        ))}
      </div>
    </div>
  );
}
