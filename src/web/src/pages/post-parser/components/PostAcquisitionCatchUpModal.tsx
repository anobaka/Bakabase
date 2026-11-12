import type { PostParserTask } from "@/core/models/PostParserTask";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionHandoffView as Handoff } from "@/sdk/Api";

import { lazy, Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Modal, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import AcquisitionHandoffNotice from "@/components/Acquire/AcquisitionHandoffNotice";
import BApi from "@/sdk/BApi";

const TaskDetail = lazy(() => import("@/components/Acquire/PlanTask/PlanTaskDetailModal"));
type Preview = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.preparePostAcquisitionCatchUp>>["data"]
>;
type Item = Preview["items"][number];
type Selection = NonNullable<Item["selection"]>;
type Identity = Pick<PostParserTask, "id" | "revision" | "workflowRunId" | "title">;
const identityKey = (task: Identity) =>
  JSON.stringify([task.id, task.revision, task.workflowRunId ?? null]);
const selectionKey = (selection: Selection) =>
  JSON.stringify([
    selection.taskId,
    selection.revision,
    selection.workflowRunId ?? null,
    selection.resultsHash,
  ]);

/** Prepares one saved result, then submits only that accepted, immutable selection. */
export default function PostAcquisitionCatchUpModal({
  task,
  onChanged,
  onDestroyed,
}: DestroyableProps & { task: Identity; onChanged: () => void }) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const captured = useRef({ ...task });
  const currentKey = useRef(identityKey(task));
  currentKey.current = identityKey(task);
  const originalKey = identityKey(captured.current);
  const identityChanged = originalKey !== currentKey.current;
  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [item, setItem] = useState<Item>();
  const [handoff, setHandoff] = useState<Handoff>();
  const [error, setError] = useState<string>();
  const [attempted, setAttempted] = useState(false);
  const mounted = useRef(false);
  const sequence = useRef(0);
  const mutating = useRef(false);
  const reading = useRef(true);
  const attemptedSelections = useRef(new Set<string>());
  const latestChanged = useRef(onChanged);
  latestChanged.current = onChanged;
  const stillCurrent = () => mounted.current && currentKey.current === originalKey;
  const validSelection = (selection?: Selection | null): selection is Selection =>
    !!selection &&
    Number.isSafeInteger(captured.current.revision) &&
    captured.current.revision! >= 0 &&
    selection.taskId === captured.current.id &&
    selection.revision === captured.current.revision &&
    (selection.workflowRunId ?? null) === (captured.current.workflowRunId ?? null) &&
    /^[a-f0-9]{64}$/i.test(selection.resultsHash);

  const load = useCallback(async () => {
    if (
      !mounted.current ||
      mutating.current ||
      currentKey.current !== identityKey(captured.current)
    )
      return;
    const request = ++sequence.current;
    reading.current = true;
    setLoading(true);
    setItem(undefined);
    setHandoff(undefined);
    setError(undefined);
    try {
      const response = await BApi.acquisition.preparePostAcquisitionCatchUp(
        { taskIds: [captured.current.id] },
        { sensitiveResponse: true, showErrorToast: false },
      );
      if (!stillCurrent() || request !== sequence.current) return;
      const next = response.data?.items;
      if (
        response.code ||
        next?.length !== 1 ||
        next[0].taskId !== captured.current.id ||
        !["eligible", "existing", "rejected"].includes(next[0].status)
      )
        throw new Error(t("postParser.catchUp.prepareFailed"));
      if (next[0].status === "eligible" && !validSelection(next[0].selection)) {
        setError(t("postParser.catchUp.changed"));
        return;
      }
      const wasAttempted =
        !!next[0].selection && attemptedSelections.current.has(selectionKey(next[0].selection));
      setAttempted(wasAttempted);
      setItem(next[0]);
      if (wasAttempted && next[0].status === "eligible")
        setError(t("postParser.catchUp.submissionUnknown"));
      if (next[0].status === "existing") latestChanged.current();
    } catch {
      if (stillCurrent() && request === sequence.current)
        setError(t("postParser.catchUp.prepareFailed"));
    } finally {
      if (stillCurrent() && request === sequence.current) {
        reading.current = false;
        setLoading(false);
      }
    }
  }, [t]);

  useEffect(() => {
    mounted.current = true;
    void load();
    return () => {
      mounted.current = false;
      sequence.current++;
    };
  }, [load]);

  useEffect(() => {
    if (!identityChanged) return;
    sequence.current++;
    setItem(undefined);
    setLoading(false);
  }, [identityChanged]);

  useEffect(() => {
    if (!item?.handoffId || item.status !== "existing" || item.acquisitionTaskIds.length) return;
    let active = true;
    const request = sequence.current;
    void BApi.acquisition
      .getAcquisitionHandoff(item.handoffId, { sensitiveResponse: true, showErrorToast: false })
      .then((response) => {
        if (
          active &&
          request === sequence.current &&
          stillCurrent() &&
          !response.code &&
          response.data?.id === item.handoffId
        )
          setHandoff(response.data);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [item]);

  const confirm = async () => {
    if (
      !stillCurrent() ||
      reading.current ||
      loading ||
      mutating.current ||
      item?.status !== "eligible" ||
      !validSelection(item.selection) ||
      attemptedSelections.current.has(selectionKey(item.selection))
    )
      return;
    const selection = { ...item.selection };
    attemptedSelections.current.add(selectionKey(selection));
    mutating.current = true;
    setSubmitting(true);
    setAttempted(true);
    setError(undefined);
    try {
      const response = await BApi.acquisition.confirmPostAcquisitionCatchUp(
        { tasks: [selection] },
        { sensitiveResponse: true, showErrorToast: false },
      );
      if (!stillCurrent()) return;
      const next = response.data?.items;
      if (
        response.code ||
        next?.length !== 1 ||
        next[0].taskId !== captured.current.id ||
        !["accepted", "existing", "rejected"].includes(next[0].status)
      )
        throw new Error();
      setItem(next[0]);
      latestChanged.current();
    } catch {
      if (stillCurrent()) {
        setError(t("postParser.catchUp.submissionUnknown"));
        latestChanged.current();
      }
    } finally {
      mutating.current = false;
      if (mounted.current) setSubmitting(false);
    }
  };
  const close = () => {
    if (mutating.current) return;
    mounted.current = false;
    sequence.current++;
    setVisible(false);
  };
  const taskIds = [
    ...new Set([
      ...(item?.acquisitionTaskIds ?? []),
      ...(handoff?.acquisitionTaskId ? [handoff.acquisitionTaskId] : []),
    ]),
  ];
  return (
    <Modal
      visible={visible}
      title={t("postParser.catchUp.title")}
      size="lg"
      hideCloseButton={submitting}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      onClose={close}
      onDestroyed={onDestroyed}
      classNames={{ footer: "flex-wrap items-center" }}
      footer={
        <>
          <Button variant="flat" isDisabled={submitting} onPress={close}>
            {t("postParser.catchUp.close")}
          </Button>
          <Button
            variant="light"
            isDisabled={loading || submitting || identityChanged}
            onPress={load}
          >
            {t("postParser.catchUp.refresh")}
          </Button>
          {item?.status === "eligible" && (
            <Button
              color="primary"
              className="h-auto min-h-10 whitespace-normal py-2"
              isDisabled={loading || submitting || attempted || identityChanged}
              isLoading={submitting}
              onPress={confirm}
            >
              {t("postParser.catchUp.confirm")}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <p className="break-words font-medium">
          #{captured.current.id} {captured.current.title}
        </p>
        <p className="text-default-600">{t("postParser.catchUp.description")}</p>
        {loading && (
          <div className="flex min-h-16 items-center justify-center">
            <Spinner label={t("postParser.catchUp.loading")} />
          </div>
        )}
        {identityChanged && (
          <p role="alert" className="text-warning">
            {t("postParser.catchUp.changed")}
          </p>
        )}
        {!identityChanged && error && (
          <p role="alert" className="text-danger">
            {error}
          </p>
        )}
        {!loading && !identityChanged && !attempted && item?.status === "eligible" && (
          <p>{t("postParser.catchUp.ready")}</p>
        )}
        {!identityChanged && item?.status === "accepted" && (
          <p role="status" className="text-success">
            {t("postParser.catchUp.accepted")}
          </p>
        )}
        {!identityChanged && item?.status === "existing" && (
          <p role="status">{t("postParser.catchUp.existing")}</p>
        )}
        {!identityChanged && item?.status === "rejected" && (
          <p role="alert" className="text-warning">
            {t(`postParser.catchUp.reason.${item.code}`, {
              defaultValue: t("postParser.catchUp.reason.unknown"),
            })}
          </p>
        )}
        {!identityChanged && !!taskIds.length && (
          <div className="flex flex-wrap gap-2">
            {taskIds.map((taskId) => (
              <Button
                key={taskId}
                size="sm"
                color="primary"
                variant="flat"
                onPress={() =>
                  createPortal(
                    ({ onDestroyed }: DestroyableProps) => (
                      <Suspense fallback={<Spinner />}>
                        <TaskDetail
                          taskId={taskId}
                          onChanged={onChanged}
                          onDestroyed={onDestroyed}
                        />
                      </Suspense>
                    ),
                    {},
                  )
                }
              >
                {t("acquire.handoff.openTask", { id: taskId })}
              </Button>
            ))}
          </div>
        )}
        {!identityChanged && handoff && !taskIds.length && (
          <AcquisitionHandoffNotice
            handoff={handoff}
            hasTaskSummary={false}
            onChanged={onChanged}
          />
        )}
      </div>
    </Modal>
  );
}
