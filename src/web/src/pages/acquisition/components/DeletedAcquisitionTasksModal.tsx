import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Modal, Pagination, Spinner } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import DeleteAcquisitionTasksModal from "./DeleteAcquisitionTasksModal";
import { deletionReasonLabel } from "./deletionReasonLabel";

type HistoryPage = NonNullable<Awaited<ReturnType<typeof BApi.acquisition.getDeletedAcquisitionTasks>>["data"]>;
type HistoryRow = HistoryPage["items"][number];
type CleanupScope = "intermediate" | "delivery";

const awaitsRelease = (row: HistoryRow, scope: CleanupScope) =>
  row[`${scope}Cleanup`] === "incomplete" && row.awaitingFileReleaseScopes?.includes(scope);
const hasPendingCleanup = (row: HistoryRow) => awaitsRelease(row, "intermediate") || awaitsRelease(row, "delivery");
const pageSize = 25;
const refreshInterval = 15_000;
const filters = ["all", "awaitingFileRelease", "incomplete", "unknown"] as const;
type HistoryFilter = typeof filters[number];

export default function DeletedAcquisitionTasksModal({ onOpenTask, onDestroyed }: DestroyableProps & {
  onOpenTask: (id: number) => void;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [visible, setVisible] = useState(true);
  const [filter, setFilter] = useState<HistoryFilter>("all");
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [selected, setSelected] = useState<number[]>([]);
  const [data, setData] = useState<HistoryPage>();
  const [failed, setFailed] = useState(false);
  const pendingOnPage = useRef(false);
  useEffect(() => {
    if (!visible) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    const load = async () => {
      try {
        const response = await BApi.acquisition.getDeletedAcquisitionTasks({ pageIndex: page, pageSize, filter },
          { signal: controller.signal, sensitiveResponse: true, showErrorToast: false });
        if (controller.signal.aborted) return;
        if (response.code || !response.data) throw new Error("DELETION_HISTORY_UNAVAILABLE");
        const lastPage = Math.max(1, Math.ceil(response.data.totalCount / pageSize));
        setFailed(false);
        if (page > lastPage) {
          pendingOnPage.current = false;
          setSelected([]);
          setData(undefined);
          setPage(lastPage);
          return;
        }
        const items = response.data.items;
        pendingOnPage.current = items.some(hasPendingCleanup);
        setData(response.data);
        setSelected(previous => previous.filter(id => items.some(row => row.taskId === id)));
      } catch {
        if (!controller.signal.aborted) setFailed(true);
      }
      if (!controller.signal.aborted && pendingOnPage.current) timer = setTimeout(load, refreshInterval);
    };
    void load();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [page, filter, revision, visible]);
  const navigate = (nextPage: number, nextFilter = filter) => {
    if (page === nextPage && filter === nextFilter) return;
    pendingOnPage.current = false;
    setData(undefined);
    setFailed(false);
    setSelected([]);
    setPage(nextPage);
    setFilter(nextFilter);
  };
  const rows = data?.items ?? [];
  const scopeLabel = (row: HistoryRow, scope: CleanupScope) => {
    const status = row[`${scope}Cleanup`];
    const label = awaitsRelease(row, scope) ? "awaitingFileRelease"
      : ["completed", "incomplete", "notRequested"].includes(status) ? status : "unknown";
    return t<string>(`acquisition.delete.history.scope.${label}`);
  };
  const retry = (ids: number[]) => {
    const chosen = rows.filter(row => ids.includes(row.taskId));
    const intermediate = chosen.every(row => row.intermediateCleanup === "incomplete" && !awaitsRelease(row, "intermediate"));
    const delivery = chosen.every(row => row.deliveryCleanup === "incomplete" && !awaitsRelease(row, "delivery"));
    createPortal(DeleteAcquisitionTasksModal, {
      taskIds: ids,
      taskNames: Object.fromEntries(chosen.map(row => [row.taskId, row.title ?? ""])),
      retryRetainedFiles: true,
      reviewWaitingRuns: chosen.some(row => row.waitingRunIds?.length),
      initialScopes: { intermediate, delivery },
      onChanged: () => setRevision(value => value + 1),
    });
  };
  return <Modal visible={visible} title={t("acquisition.delete.history.title")} size="4xl" onClose={() => setVisible(false)} onDestroyed={onDestroyed}>
    <div className="flex flex-wrap items-center gap-2">
      {filters.map(value => <Button key={value} size="sm" variant={filter === value ? "solid" : "flat"} aria-pressed={filter === value}
        onPress={() => navigate(1, value)}>{t(`acquisition.delete.history.filter.${value}`)}</Button>)}
    </div>
    {rows.some(hasPendingCleanup) && <p className="text-sm text-default-600">{t("acquisition.delete.history.automaticCleanup")}</p>}
    {failed && <div role="alert">{t("acquisition.delete.history.unavailable")} <Button size="sm" onPress={() => setRevision(value => value + 1)}>{t("common.action.retry")}</Button></div>}
    {!data && !failed && <Spinner aria-label={t("acquisition.plan.loading")} />}
    {data && (rows.length === 0 ? <p>{t("acquisition.delete.history.empty")}</p> : <>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <Checkbox size="sm" isSelected={selected.length === rows.length} isIndeterminate={selected.length > 0 && selected.length < rows.length}
            onValueChange={checked => setSelected(checked ? rows.map(row => row.taskId) : [])}>{t("acquisition.delete.history.selectPage")}</Checkbox>
          <Button size="sm" variant="flat" isDisabled={!selected.length} onPress={() => retry(selected)}>{t("acquisition.delete.retained.title")}</Button>
        </div>
        <ul className="max-h-[55vh] divide-y divide-default-200 overflow-auto">
          {rows.map(row => {
            const reasons = row.lastReasonCodes.filter(code => code !== "CLEANUP_SMB_AUTO_PENDING" || !hasPendingCleanup(row));
            return <li key={row.taskId} className="flex items-start gap-2 py-2">
              <Checkbox size="sm" aria-label={`#${row.taskId}`} isSelected={selected.includes(row.taskId)}
                onValueChange={checked => setSelected(prev => checked ? [...prev, row.taskId] : prev.filter(id => id !== row.taskId))} />
              <div className="min-w-0 flex-1">
                <button type="button" className="text-left font-medium text-primary [overflow-wrap:anywhere]" onClick={() => onOpenTask(row.taskId)}>#{row.taskId} {row.title}</button>
                <p className="text-xs text-default-500">{new Date(row.deletedAt).toLocaleString()}</p>
                <p className="text-sm">{t("acquisition.delete.history.scopes", { intermediate: scopeLabel(row, "intermediate"), delivery: scopeLabel(row, "delivery") })}</p>
                {!!row.waitingRunIds?.length && <p className="text-sm text-warning-600">{t("acquisition.delete.history.waitingRuns", {
                  runs: row.waitingRunIds.map(id => `#${id}`).join(", "),
                })}</p>}
                {!!reasons.length && <p className="text-xs text-warning-600">{t("acquisition.delete.history.lastReasons", { reasons: reasons.map(code => deletionReasonLabel(t, code)).join(" · ") })}</p>}
              </div>
              <Button size="sm" variant="light" onPress={() => retry([row.taskId])}>{t("acquisition.delete.history.review")}</Button>
            </li>;
          })}
        </ul>
        {data.totalCount > pageSize && <Pagination aria-label={t("acquisition.delete.pagination")} page={page} total={Math.ceil(data.totalCount / pageSize)} onChange={next => navigate(next)} />}
      </>)}
  </Modal>;
}
