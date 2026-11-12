import type { DestroyableProps } from "@/components/bakaui/types";
import type { TaskFilterScope } from "./TaskFilterScopeSummary";
import type { CatchUpItem, CatchUpPreview, CatchUpResult } from "../batchCatchUp";

import { lazy, Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineInfoCircle } from "react-icons/ai";

import { Button, Checkbox, Modal, Pagination, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ExternalLink from "@/components/ExternalLink";
import BApi from "@/sdk/BApi";
import TaskFilterScopeSummary from "./TaskFilterScopeSummary";
import Tooltip from "./PostParserTooltip";
import { catchUpSelectionKey, isValidCatchUpPreview, isValidCatchUpResult } from "../batchCatchUp";

const TaskDetail = lazy(() => import("@/components/Acquire/PlanTask/PlanTaskDetailModal"));
const pageSize = 20;
const statuses = ["eligible", "existing", "rejected", "accepted", "unknown"] as const;
type Status = (typeof statuses)[number];

/** This dialog freezes the filter's IDs once; refresh and confirmation never search again. */
export default function PostAcquisitionBatchCatchUpModal({
  scope,
  onChanged,
  onClosed,
  onDestroyed,
}: DestroyableProps & {
  scope: TaskFilterScope;
  onChanged: () => void;
  onClosed?: () => void;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const label = (key: string, values?: Record<string, unknown>) =>
    t(`postParser.batchCatchUp.${key}`, values);
  const captured = useRef({
    keyword: scope.keyword?.trim() || undefined,
    status: scope.status ?? "all",
    reasonCode: scope.reasonCode,
  });
  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [preview, setPreview] = useState<CatchUpPreview>();
  const [result, setResult] = useState<CatchUpResult>();
  const [limit, setLimit] = useState<{ matched: number; max: number }>();
  const [error, setError] = useState<string>();
  const [selected, setSelected] = useState<Set<number>>(() => new Set());
  const [filter, setFilter] = useState<Status | "all">("all");
  const [page, setPage] = useState(1);
  const frozenTaskIds = useRef<number[]>();
  const lastSnapshot = useRef<CatchUpPreview>();
  const attempted = useRef(new Set<string>());
  const mounted = useRef(false);
  const sequence = useRef(0);
  const reading = useRef(true);
  const mutating = useRef(false);
  const latestSelected = useRef(selected);
  const latestChanged = useRef(onChanged);
  latestSelected.current = selected;
  latestChanged.current = onChanged;

  const statusOf = (item: CatchUpItem): Status =>
    item.status === "eligible" &&
    item.selection &&
    attempted.current.has(catchUpSelectionKey(item.selection))
      ? "unknown"
      : (item.status as Status);

  const load = useCallback(async () => {
    if (!mounted.current || mutating.current) return;
    const firstSnapshot = frozenTaskIds.current == null;
    const previouslySelected = new Set(
      (lastSnapshot.current?.items ?? [])
        .filter((item) => item.selection && latestSelected.current.has(item.taskId))
        .map((item) => catchUpSelectionKey(item.selection!)),
    );
    const request = ++sequence.current;
    reading.current = true;
    setLoading(true);
    setPreview(undefined);
    setResult(undefined);
    setError(undefined);
    setLimit(undefined);
    try {
      let next: CatchUpPreview | undefined;
      if (frozenTaskIds.current) {
        if (!frozenTaskIds.current.length) {
          next = {
            requestedCount: 0,
            eligibleCount: 0,
            existingCount: 0,
            rejectedCount: 0,
            items: [],
          };
        } else {
          const response = await BApi.acquisition.preparePostAcquisitionCatchUp(
            { taskIds: [...frozenTaskIds.current] },
            { sensitiveResponse: true, showErrorToast: false },
          );
          if (response.code) throw new Error();
          next = response.data;
        }
      } else {
        const response = await BApi.acquisition.prepareFilteredPostAcquisitionCatchUp(
          captured.current,
          { sensitiveResponse: true, showErrorToast: false },
        );
        if (!mounted.current || request !== sequence.current) return;
        const data = response.data;
        if (
          response.code ||
          !data ||
          !Number.isSafeInteger(data.matchedCount) ||
          data.matchedCount < 0 ||
          !Number.isSafeInteger(data.maxTasks) ||
          data.maxTasks < 1
        )
          throw new Error();
        if (data.limitExceeded) {
          if (data.matchedCount <= data.maxTasks || data.preview) throw new Error();
          setLimit({ matched: data.matchedCount, max: data.maxTasks });
          return;
        }
        next = data.preview;
        if (data.matchedCount > data.maxTasks || next?.requestedCount !== data.matchedCount)
          throw new Error();
      }
      if (!mounted.current || request !== sequence.current) return;
      if (!isValidCatchUpPreview(next, frozenTaskIds.current)) throw new Error();
      // Own the immutable values shown for review, independently of cache or live row updates.
      const snapshot = structuredClone(next);
      frozenTaskIds.current ??= snapshot.items.map((item) => item.taskId);
      lastSnapshot.current = snapshot;
      setPreview(snapshot);
      const selection = new Set(
        snapshot.items
          .filter(
            (item) =>
              item.status === "eligible" &&
              item.selection &&
              !attempted.current.has(catchUpSelectionKey(item.selection)) &&
              (firstSnapshot || previouslySelected.has(catchUpSelectionKey(item.selection))),
          )
          .map((item) => item.taskId),
      );
      latestSelected.current = selection;
      setSelected(selection);
      setPage(1);
    } catch {
      if (mounted.current && request === sequence.current)
        setError(t("postParser.catchUp.prepareFailed"));
    } finally {
      if (mounted.current && request === sequence.current) {
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

  const changeSelection = (next: Set<number>) => {
    if (reading.current || mutating.current) return;
    latestSelected.current = next;
    setSelected(next);
  };
  const confirm = async () => {
    if (!mounted.current || reading.current || mutating.current || !preview) return;
    const tasks = preview.items
      .filter((item) => statusOf(item) === "eligible" && latestSelected.current.has(item.taskId))
      .map((item) => ({ ...item.selection! }));
    if (!tasks.length) return;
    for (const selection of tasks) attempted.current.add(catchUpSelectionKey(selection));
    latestSelected.current = new Set();
    setSelected(new Set());
    mutating.current = true;
    setSubmitting(true);
    setError(undefined);
    try {
      const response = await BApi.acquisition.confirmPostAcquisitionCatchUp(
        { tasks },
        { sensitiveResponse: true, showErrorToast: false },
      );
      if (!mounted.current) return;
      if (response.code || !isValidCatchUpResult(response.data, tasks)) throw new Error();
      const outcome = structuredClone(response.data);
      const byId = new Map(outcome.items.map((item) => [item.taskId, item]));
      setResult(outcome);
      const updated = {
        ...preview,
        items: preview.items.map((item) => {
          const update = byId.get(item.taskId);
          return update ? { ...item, ...update, title: item.title, link: item.link } : item;
        }),
      };
      lastSnapshot.current = updated;
      setPreview(updated);
      latestSelected.current = new Set();
      setSelected(new Set());
      latestChanged.current();
    } catch {
      if (mounted.current) {
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
    onClosed?.();
  };
  const items = preview?.items ?? [];
  const counts = Object.fromEntries(
    statuses.map((status) => [status, items.filter((item) => statusOf(item) === status).length]),
  ) as Record<Status, number>;
  const selectable = items.filter((item) => statusOf(item) === "eligible");
  const selectedCount = selectable.filter((item) => selected.has(item.taskId)).length;
  const filtered = filter === "all" ? items : items.filter((item) => statusOf(item) === filter);
  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const currentPage = Math.min(page, totalPages);
  const shown = filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const busy = loading || submitting;

  return (
    <Modal
      visible={visible}
      title={label("title")}
      size="5xl"
      classNames={{
        base: "max-h-[calc(100dvh-2rem)]",
        header: "px-4 py-3",
        body: "min-h-0 gap-2 overflow-hidden px-4 py-2",
        footer: "flex-wrap items-center gap-2 border-t border-default-200 px-4 py-3",
      }}
      hideCloseButton={submitting}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      onClose={close}
      onDestroyed={onDestroyed}
      footer={
        <>
          <span className="mr-auto text-sm text-default-500">
            {label("selected", { count: selectedCount })}
          </span>
          <Button variant="flat" isDisabled={submitting} onPress={close}>
            {t("postParser.catchUp.close")}
          </Button>
          <Button variant="light" isDisabled={busy} onPress={load}>
            {label("refresh")}
          </Button>
          <Button
            color="primary"
            className="h-auto min-h-10 whitespace-normal py-2"
            isDisabled={busy || !selectedCount || !!error}
            isLoading={submitting}
            onPress={confirm}
          >
            {label("confirm", { count: selectedCount })}
          </Button>
        </>
      }
    >
      <div className="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-1 text-xs text-default-500">
        <span>{label("description")}</span>
        <Tooltip content={<TaskFilterScopeSummary {...captured.current} />}>
          <Button size="sm" variant="light" className="h-6 min-w-0 px-1.5 text-xs">
            {label("scope")}
          </Button>
        </Tooltip>
        <Tooltip content={label("details")}>
          <Button
            isIconOnly
            size="sm"
            variant="light"
            className="h-6 w-6 min-w-0"
            aria-label={label("detailsLabel")}
          >
            <AiOutlineInfoCircle aria-hidden />
          </Button>
        </Tooltip>
      </div>
      {loading && (
        <div className="flex min-h-32 items-center justify-center">
          <Spinner label={t("postParser.catchUp.loading")} />
        </div>
      )}
      {error && (
        <p role="alert" className="shrink-0 text-sm text-danger">
          {error}
        </p>
      )}
      {limit && (
        <p role="alert" className="shrink-0 text-sm text-warning">
          {label("limit", { count: limit.matched, max: limit.max })}
        </p>
      )}
      {result && (
        <p role="status" className="shrink-0 text-sm text-success">
          {label("result", {
            accepted: result.acceptedCount,
            existing: result.existingCount,
            rejected: result.rejectedCount,
          })}
        </p>
      )}
      {!!counts.unknown && !error && (
        <p role="alert" className="shrink-0 text-sm text-warning">
          {t("postParser.catchUp.submissionUnknown")}
        </p>
      )}
      {preview && (
        <>
          <div
            role="group"
            aria-label={label("statuses")}
            className="flex shrink-0 flex-wrap items-center gap-1"
          >
            {(["all", ...statuses] as const)
              .filter(
                (status) => !["accepted", "unknown"].includes(status) || counts[status as Status],
              )
              .map((status) => (
                <Button
                  key={status}
                  size="sm"
                  className="h-7 min-w-0 px-2 text-xs"
                  variant={filter === status ? "flat" : "light"}
                  color={filter === status ? "primary" : "default"}
                  aria-pressed={filter === status}
                  isDisabled={busy}
                  onPress={() => {
                    setFilter(status);
                    setPage(1);
                  }}
                >
                  {label(`status.${status}`)} {status === "all" ? items.length : counts[status]}
                </Button>
              ))}
          </div>
          {!!items.length && (
            <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-b border-default-200 pb-2 text-xs">
              <Checkbox
                size="sm"
                isSelected={!!selectable.length && selectedCount === selectable.length}
                isIndeterminate={selectedCount > 0 && selectedCount < selectable.length}
                isDisabled={busy || !selectable.length}
                onValueChange={(value) =>
                  changeSelection(new Set(value ? selectable.map((item) => item.taskId) : []))
                }
              >
                {label("selectAll", { count: selectable.length })}
              </Checkbox>
              <span className="text-default-500">{label("snapshot")}</span>
            </div>
          )}
          {!shown.length ? (
            <p className="py-8 text-center text-sm text-default-500">{label("empty")}</p>
          ) : (
            <ul
              aria-label={label("list")}
              className="min-h-0 flex-1 divide-y divide-default-200 overflow-y-auto"
            >
              {shown.map((item) => {
                const status = statusOf(item);
                return (
                  <li
                    key={item.taskId}
                    data-catch-up-task-id={item.taskId}
                    className="flex items-start gap-3 py-2"
                  >
                    <Checkbox
                      aria-label={label("selectTask", { id: item.taskId })}
                      className="mt-0.5 shrink-0"
                      size="sm"
                      isSelected={status === "eligible" && selected.has(item.taskId)}
                      isDisabled={busy || status !== "eligible"}
                      onValueChange={(value) => {
                        const next = new Set(latestSelected.current);
                        if (value) next.add(item.taskId);
                        else next.delete(item.taskId);
                        changeSelection(next);
                      }}
                    />
                    <div className="min-w-0 flex-1 space-y-0.5 text-sm">
                      {item.link ? (
                        <ExternalLink href={item.link} className="break-words text-sm" size="sm">
                          #{item.taskId} {item.title}
                        </ExternalLink>
                      ) : (
                        <p className="break-words">
                          #{item.taskId} {item.title}
                        </p>
                      )}
                      {status === "rejected" && (
                        <p className="text-xs text-warning">
                          {t(`postParser.catchUp.reason.${item.code}`, {
                            defaultValue: t("postParser.catchUp.reason.unknown"),
                          })}
                        </p>
                      )}
                      {!!item.acquisitionTaskIds?.length && (
                        <div className="flex flex-wrap gap-1">
                          {[...new Set(item.acquisitionTaskIds)].map((taskId) => (
                            <Button
                              key={taskId}
                              size="sm"
                              variant="light"
                              color="primary"
                              className="h-6 min-w-0 px-1 text-xs"
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
                    </div>
                    <span
                      className={`shrink-0 text-xs ${status === "rejected" || status === "unknown" ? "text-warning" : "text-default-500"}`}
                    >
                      {label(`status.${status}`)}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
          {totalPages > 1 && (
            <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t border-default-200 pt-2">
              <span className="text-xs text-default-500">
                {label("page", { page: currentPage, pages: totalPages, count: filtered.length })}
              </span>
              <Pagination
                aria-label={label("pagination")}
                size="sm"
                page={currentPage}
                total={totalPages}
                showControls
                isDisabled={busy}
                onChange={setPage}
              />
            </div>
          )}
        </>
      )}
    </Modal>
  );
}
