import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FiCloud } from "react-icons/fi";
import { Button, Modal } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { downloadProblemSummary } from "@/components/CloudDownload/problemLabels";
import BApi from "@/sdk/BApi";
import type { CenterTask } from "../downloadCenter";

type OwnedCopy = NonNullable<CenterTask["ownedCopy"]>;

export function OwnedCloudCopyButton({ copy }: { copy: OwnedCopy }) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const state = t<string>(`downloadCenter.ownedCopy.state.${copy.status}`, {
    defaultValue: t("downloadCenter.ownedCopy.state.unknown"),
  });
  return (
    <Button
      data-task-action
      size="sm"
      variant="flat"
      className="h-6 min-w-0 shrink-0 gap-1 px-2 text-xs"
      color={
        copy.status === "reviewRequired" || copy.status === "retryable" ? "warning" : "default"
      }
      aria-label={t("downloadCenter.ownedCopy.show", { state })}
      title={t("downloadCenter.ownedCopy.show", { state })}
      onPress={() => createPortal(OwnedCloudCopyModal, { taskId: copy.downloadTaskId })}
    >
      <FiCloud aria-hidden />
      {state}
    </Button>
  );
}

export function OwnedCloudCopyModal({
  taskId,
  onDestroyed,
}: DestroyableProps & { taskId: number }) {
  const { t } = useTranslation();
  const [copy, setCopy] = useState<OwnedCopy>();
  const [problem, setProblem] = useState<string>();
  const [busy, setBusy] = useState(false);
  const controller = useRef<AbortController>();
  const request = async (retry: boolean) => {
    if (controller.current) return;
    const active = new AbortController();
    controller.current = active;
    setBusy(true);
    setProblem(undefined);
    try {
      const options = { signal: active.signal, showErrorToast: false };
      const response = retry
        ? await BApi.downloadCenter.retryCloudDownloadOwnedCopy(taskId, options)
        : await BApi.downloadCenter.getCloudDownloadOwnedCopy(taskId, options);
      if (active.signal.aborted) return;
      if (response.code || !response.data || response.data.downloadTaskId !== taskId) {
        throw new Error(response.message || "");
      }
      setCopy(response.data);
    } catch (error) {
      if (!active.signal.aborted) {
        // A failed submission can have reached the provider. Refresh the receipt before another retry.
        setCopy(undefined);
        setProblem(
          downloadProblemSummary(error instanceof Error ? error.message : "", t) ||
            t(
              retry
                ? "downloadCenter.ownedCopy.retryFailed"
                : "downloadCenter.ownedCopy.loadFailed",
            ),
        );
      }
    } finally {
      if (controller.current === active) {
        controller.current = undefined;
        setBusy(false);
      }
    }
  };
  useEffect(() => {
    setCopy(undefined);
    void request(false);
    return () => {
      controller.current?.abort();
      controller.current = undefined;
    };
  }, [taskId]);

  useEffect(() => {
    if (copy?.status !== "pending" && copy?.status !== "waitingDownload") return;
    const timer = setInterval(() => {
      if (!document.hidden) void request(false);
    }, 3000);
    return () => clearInterval(timer);
  }, [taskId, copy?.status]);

  const reason = copy?.reasonCode && downloadProblemSummary(copy.reasonCode, t);
  return (
    <Modal
      defaultVisible
      title={t("downloadCenter.ownedCopy.title")}
      onDestroyed={onDestroyed}
      footer={
        <>
          <Button size="sm" variant="light" isDisabled={busy} onPress={() => void request(false)}>
            {t("cloudDownload.account.refresh")}
          </Button>
          {copy?.canRetry && (
            <Button size="sm" color="primary" isLoading={busy} onPress={() => void request(true)}>
              {t("downloadCenter.ownedCopy.retry")}
            </Button>
          )}
        </>
      }
    >
      {problem && (
        <p role="alert" className="text-sm text-danger">
          {problem}
        </p>
      )}
      {busy && !copy && <p role="status">{t("common.state.loading")}</p>}
      {copy && (
        <div className="space-y-2 text-sm">
          <p role="status" className="font-medium">
            {t(`downloadCenter.ownedCopy.state.${copy.status}`, {
              defaultValue: t("downloadCenter.ownedCopy.state.unknown"),
            })}
          </p>
          <p className="text-default-600">
            {t(`downloadCenter.ownedCopy.hint.${copy.status}`, {
              defaultValue: t("downloadCenter.ownedCopy.hint.unknown"),
            })}
          </p>
          {reason && <p className="text-default-600">{reason}</p>}
        </div>
      )}
    </Modal>
  );
}
