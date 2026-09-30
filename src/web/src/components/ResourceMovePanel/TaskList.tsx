import type { MoveBatch, MoveRecord } from "./types";

import { useEffect, useState } from "react";

import { movePanelApi } from "./api";
import { getKnownMoveReasonCode, useMoveReasonText, useMoveText } from "./messages";
import { moveSourceContextError } from "./sourceContext";

import { Modal } from "@/components/bakaui";
import { useBTasksStore } from "@/stores/bTasks";
import { refreshMovePanel, useResourceMovePanelStore } from "@/stores/resourceMovePanel";

function ConflictActions({
  record,
  onAction,
}: {
  record: MoveRecord;
  onAction: (action: () => Promise<unknown>) => void;
}) {
  const [scope, setScope] = useState<"once" | "batch" | "panel">("once");
  const text = useMoveText();

  return (
    <div className="move-panel-conflict">
      <strong>{text("waiting")}</strong>
      <code>{record.conflictPath ?? record.destPath}</code>
      {record.canOverwrite && (
        <>
          <label>
            {text("scope")}
            <select value={scope} onChange={(e) => setScope(e.target.value as typeof scope)}>
              <option value="once">{text("once")}</option>
              <option value="batch">{text("batch")}</option>
              <option value="panel">{text("panel")}</option>
            </select>
          </label>
          {scope === "panel" && <p>{text("autoHelp")}</p>}
          <button
            className="danger"
            type="button"
            onClick={() =>
              onAction(() =>
                movePanelApi.resolve(record.id, "overwrite", scope, record.conflictVersion ?? 0),
              )
            }
          >
            {text("resolveOverwrite")}
          </button>
        </>
      )}
      <button
        type="button"
        onClick={() =>
          onAction(() =>
            movePanelApi.resolve(record.id, "skip", "once", record.conflictVersion ?? 0),
          )
        }
      >
        {text("skip")}
      </button>
    </div>
  );
}
/** Legacy records can only be released after explicitly confirming and verifying manual restoration. */
function LegacyRecoveryActions({ record }: { record: MoveRecord }) {
  const text = useMoveText();
  const reasonText = useMoveReasonText();
  const sourceContext = useResourceMovePanelStore((s) => s.sourceContext);
  const restorePlatformLinks =
    record.errorCode === "legacySourcePlanMissing" ||
    record.conflictKind === "legacySourcePlanMissing";
  const [confirming, setConfirming] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const close = () => {
    if (!busy) {
      setConfirming(false);
      setAcknowledged(false);
      setError(undefined);
    }
  };
  const confirm = async () => {
    if (!acknowledged || busy) return;
    setBusy(true);
    setError(undefined);
    try {
      const contextError = moveSourceContextError(sourceContext, await movePanelApi.context());

      if (contextError) throw new Error(contextError);
      await movePanelApi.resolve(record.id, "restoreSource", "once", record.conflictVersion ?? 0);
      await refreshMovePanel();
      setConfirming(false);
      setAcknowledged(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <button type="button" onClick={() => setConfirming(true)}>
        {text(restorePlatformLinks ? "restorePlatformSource" : "restoreSource")}
      </button>
      {confirming && (
        <Modal
          visible
          footer={false}
          hideCloseButton={busy}
          isDismissable={!busy}
          isKeyboardDismissDisabled={busy}
          title={text(restorePlatformLinks ? "restorePlatformSourceTitle" : "restoreSourceTitle")}
          onClose={close}
        >
          <div data-resource-move-panel className="move-panel-confirm">
            <p>{text(restorePlatformLinks ? "restorePlatformSourceHelp" : "restoreSourceHelp")}</p>
            <code className="move-panel-full-path">{record.sourcePath}</code>
            <label>
              <input
                checked={acknowledged}
                disabled={busy}
                type="checkbox"
                onChange={(e) => setAcknowledged(e.target.checked)}
              />
              {text(
                restorePlatformLinks
                  ? "restorePlatformSourceAcknowledgement"
                  : "restoreSourceAcknowledgement",
              )}
            </label>
            {error && (
              <p className="move-panel-error" role="alert">
                {getKnownMoveReasonCode(error) ? reasonText(error) : error}
              </p>
            )}
            <div className="move-panel-actions">
              <button disabled={busy} type="button" onClick={close}>
                {text("cancel")}
              </button>
              <button
                className="primary"
                disabled={!acknowledged || busy}
                type="button"
                onClick={() => void confirm()}
              >
                {text(restorePlatformLinks ? "restorePlatformSourceSubmit" : "restoreSourceSubmit")}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </>
  );
}
export function MoveTaskCard({ batch }: { batch: MoveBatch }) {
  const text = useMoveText();
  const reasonText = useMoveReasonText();
  const sourceContext = useResourceMovePanelStore((s) => s.sourceContext);
  const task = useBTasksStore((s) =>
    s.tasks.find((t) => t.id === batch.taskId || t.id === batch.batchId),
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const act = async (fn: () => Promise<unknown>) => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      const contextError = moveSourceContextError(sourceContext, await movePanelApi.context());

      if (contextError) throw new Error(contextError);
      await fn();
      await refreshMovePanel();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };
  const counts = batch.counts ?? {
    total: batch.records.length,
    succeeded: 0,
    failed: 0,
    cancelled: 0,
    skipped: 0,
    waiting: 0,
  };
  const progress = Math.min(
    100,
    Math.max(
      0,
      batch.status === "running"
        ? (task?.percentage ?? batch.percentage ?? 0)
        : (batch.percentage ?? 0),
    ),
  );
  const hasConflicts = batch.records.some(
    (r) =>
      r.status === 7 ||
      r.conflictKind === "legacyRecovery" ||
      r.conflictKind === "legacySourcePlanMissing" ||
      r.errorCode === "legacySourcePlanMissing",
  );

  return (
    <article
      className={`move-panel-task status-${batch.status}`}
      data-testid={`move-task-${batch.batchId}`}
    >
      <div className="move-panel-task-heading">
        <strong title={batch.destDir}>{batch.destinationName || batch.destDir}</strong>
        <span className="move-panel-badge">{text(batch.status)}</span>
      </div>
      {batch.destinationName && <code title={batch.destDir}>{batch.destDir}</code>}
      <div className="move-panel-muted">
        {text("taskSource")}: {batch.sourceTabName || batch.sourceTabId || text("noTab")}
      </div>
      <div className="move-panel-progress">
        <progress aria-label={text("tasks")} max={100} value={progress} />
        <span>{Math.round(progress)}%</span>
      </div>
      <div className="move-panel-counts">
        {text("succeeded")} {counts.succeeded}/{counts.total}
        {counts.failed > 0 && ` · ${text("failed")} ${counts.failed}`}
        {counts.skipped > 0 && ` · ${text("skipped")} ${counts.skipped}`}
        {counts.cancelled > 0 && ` · ${text("cancelled")} ${counts.cancelled}`}
      </div>
      {batch.status === "stopping" && <p className="move-panel-notice">{text("stoppingHelp")}</p>}
      {error && (
        <p className="move-panel-error" role="alert">
          {getKnownMoveReasonCode(error) ? reasonText(error) : error}
        </p>
      )}
      <fieldset className="move-panel-task-actions" disabled={busy}>
        {batch.canCancel && (
          <button type="button" onClick={() => void act(() => movePanelApi.cancel(batch.batchId))}>
            {text("stop")}
          </button>
        )}
        {batch.canRetry && (
          <button type="button" onClick={() => void act(() => movePanelApi.retry(batch.batchId))}>
            {text("retry")}
          </button>
        )}
        <details open={hasConflicts || undefined}>
          <summary>
            {text("details")} ({batch.records.length})
          </summary>
          {batch.records.map((record) => (
            <div key={record.id} className="move-panel-record">
              <code>{record.sourcePath}</code>
              <code>→ {record.destPath}</code>
              {(record.error || record.errorCode) && (
                <p className="move-panel-error">
                  {getKnownMoveReasonCode(record.errorCode ?? record.error)
                    ? reasonText(record.errorCode ?? record.error)
                    : record.error}
                </p>
              )}
              {record.conflictKind === "legacyRecovery" ||
              record.conflictKind === "legacySourcePlanMissing" ||
              record.errorCode === "legacySourcePlanMissing" ? (
                <LegacyRecoveryActions record={record} />
              ) : (
                record.status === 7 && (
                  <ConflictActions record={record} onAction={(fn) => void act(fn)} />
                )
              )}
            </div>
          ))}
        </details>
      </fieldset>
    </article>
  );
}
export default function TaskList() {
  const text = useMoveText();
  const reasonText = useMoveReasonText();
  const sourceContext = useResourceMovePanelStore((s) => s.sourceContext);
  const contextInvalidated = useResourceMovePanelStore((s) => s.sourceContextInvalidated);
  const batches = useResourceMovePanelStore((s) => s.batches);
  const tabId = useResourceMovePanelStore((s) => s.context.tabId);
  const [filterCurrent, setFilterCurrent] = useState(false);
  const [moreLoading, setMoreLoading] = useState(false);
  const [moreError, setMoreError] = useState<string>();
  const [history, setHistory] = useState<MoveBatch[]>([]);
  const [nextPage, setNextPage] = useState(100);
  const [hasMore, setHasMore] = useState(true);

  useEffect(() => {
    setHistory([]);
    setNextPage(100);
    setHasMore(true);
  }, [sourceContext?.nodeId, sourceContext?.libraryEpoch]);
  const all = [...new Map([...history, ...batches].map((b) => [b.batchId, b])).values()];
  const shown = all.filter((b) => !filterCurrent || b.sourceTabId === tabId);
  const otherWaiting = all.filter(
    (b) => b.sourceTabId !== tabId && (b.status === "waiting" || b.status === "needsRecovery"),
  ).length;
  const more = async () => {
    setMoreLoading(true);
    setMoreError(undefined);
    try {
      const result = await movePanelApi.batches(nextPage);

      setHistory((previous) => [...previous, ...result]);
      setHasMore(result.length >= 100);
      setNextPage((p) => p + result.length);
    } catch (e) {
      setMoreError(e instanceof Error ? e.message : String(e));
    } finally {
      setMoreLoading(false);
    }
  };

  if (contextInvalidated || !sourceContext)
    return (
      <p className="move-panel-notice">
        {reasonText(contextInvalidated ? "sourceContextChanged" : "sourceContextRequired")}
      </p>
    );

  return (
    <section className="move-panel-task-section">
      <div className="move-panel-section-heading">
        <h3>
          {text("tasks")} ({all.length})
        </h3>
        <select
          aria-label={text("tasks")}
          value={filterCurrent ? "current" : "all"}
          onChange={(e) => setFilterCurrent(e.target.value === "current")}
        >
          <option value="all">{text("allTasks")}</option>
          {tabId && <option value="current">{text("currentTasks")}</option>}
        </select>
      </div>
      {filterCurrent && otherWaiting > 0 && (
        <button className="move-panel-notice" type="button" onClick={() => setFilterCurrent(false)}>
          {text("pendingOther")}: {otherWaiting}
        </button>
      )}
      <div className="move-panel-task-list">
        {shown.length ? (
          shown.map((batch) => <MoveTaskCard key={batch.batchId} batch={batch} />)
        ) : (
          <p className="move-panel-empty">{text("noTasks")}</p>
        )}
        {hasMore && batches.length >= 100 && (
          <button disabled={moreLoading} type="button" onClick={() => void more()}>
            {text("more")}
          </button>
        )}
        {moreError && (
          <p className="move-panel-error" role="alert">
            {moreError}
          </p>
        )}
      </div>
    </section>
  );
}
