import { lazy, Suspense, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionHandoffView as Handoff } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";
import { Button, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const TaskDetail = lazy(() => import("./PlanTask/PlanTaskDetailModal"));
const statuses = new Set(["queued", "prepared", "created", "attention", "error"]);

/** Shows the durable handoff separately from the acquisition's own execution state. */
export default function AcquisitionHandoffNotice({
  handoff,
  hasTaskSummary,
  hasStartedTask = false,
  onChanged,
  onRetry,
}: {
  handoff?: Handoff | null;
  hasTaskSummary: boolean;
  hasStartedTask?: boolean;
  onChanged: () => void;
  onRetry?: () => Promise<void>;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const pending = useRef(false);
  if (!handoff || hasStartedTask || (handoff.status === "created" && hasTaskSummary)) return null;
  const needsInput = ["attention", "error"].includes(handoff.status);
  const key = handoff.code ? `acquire.handoff.issue.${handoff.code}` : undefined;
  const retry = async () => {
    if (pending.current || !onRetry) return;
    pending.current = true;
    setBusy(true);
    setError(false);
    try {
      await onRetry();
    } catch {
      setError(true);
    } finally {
      pending.current = false;
      setBusy(false);
    }
  };

  return (
    <div
      className={`space-y-2 rounded-lg p-2 text-xs ${needsInput ? "bg-warning/10" : "bg-default-100"}`}
    >
      <p className="font-medium">
        {t(`acquire.handoff.status.${statuses.has(handoff.status) ? handoff.status : "unknown"}`)}
      </p>
      {needsInput && (
        <p className="leading-relaxed text-default-600">
          {t(key ?? "acquire.handoff.issue.unknown", {
            defaultValue: t(`acquire.requirement.${handoff.code}`, {
              defaultValue: t("acquire.handoff.issue.unknown"),
            }),
          })}
        </p>
      )}
      {!!handoff.acquisitionTaskId && !hasTaskSummary && (
        <Button
          color="primary"
          size="sm"
          variant="flat"
          onPress={() =>
            createPortal(
              ({ onDestroyed }: DestroyableProps) => (
                <Suspense fallback={<Spinner />}>
                  <TaskDetail
                    taskId={handoff.acquisitionTaskId!}
                    onDestroyed={onDestroyed}
                    onChanged={onChanged}
                  />
                </Suspense>
              ),
              {},
            )
          }
        >
          {t("acquire.handoff.openTask", { id: handoff.acquisitionTaskId })}
        </Button>
      )}
      {needsInput && !handoff.acquisitionTaskId && onRetry && (
        <Button size="sm" variant="flat" isDisabled={busy} isLoading={busy} onPress={retry}>
          {t("acquire.handoff.retry")}
        </Button>
      )}
      {error && (
        <p className="text-danger" role="alert">
          {t("acquire.handoff.retryFailed")}
        </p>
      )}
    </div>
  );
}
