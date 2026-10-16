import type { ActivityPage, SyncActivity, ActivityFilters, MetadataRun } from "../activityApi";
import type { ProcessingJob } from "@/pages/resource-profile/processingApi";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDown, AiOutlineReload, AiOutlineRight } from "react-icons/ai";

import { activityApi } from "../activityApi";
import { dataSyncApi } from "../api";
import { localDateTime } from "../times";

import { panelClass, smallButtonClass } from "./common";
import ActivityErrorNotice, { ErrorExplanation } from "./ActivityErrorNotice";

import DeviceDot from "@/features/management/DeviceDot";
import { processingActions, processingApi } from "@/pages/resource-profile/processingApi";
import { Button, Select } from "@/components/bakaui";

const states = [
  "Current",
  "ReportedCurrent",
  "Syncing",
  "Running",
  "Queued",
  "NeedsReview",
  "Conflict",
  "Failed",
  "Offline",
  "Paused",
  "Waiting",
  "Denied",
  "UpdateRequired",
  "IdentityChanged",
  "Completed",
  "Cancelled",
  "Stopped",
];
const attention = new Set([
  "NeedsReview",
  "Conflict",
  "Failed",
  "Denied",
  "UpdateRequired",
  "IdentityChanged",
]);
const phases = new Set([
  "Connecting",
  "Comparing",
  "PeerReport",
  "Snapshot",
  "Changes",
  "Refreshing",
  "Completed",
  "Checkpointed",
  "Interrupted",
]);
const processingPhases = new Set([
  "Scanning",
  "Preparing",
  "Processing",
  "DiscoveringFiles",
  "CheckingFiles",
  "FilesDiscovered",
  "EnhancementComputed",
  "Transferring",
  "Applying",
  "WaitingForDevice",
  "WaitingForWorker",
  "Completed",
]);

export default function SyncActivityCenter({
  version,
  onReviewDefinitions,
  onDefinitionHistory,
}: {
  version: number;
  onReviewDefinitions: (peer: string, kind: string) => void;
  onDefinitionHistory: (peer: string) => void;
}) {
  const { t } = useTranslation();
  const [filters, setFilters] = useState<ActivityFilters>({});
  const [page, setPage] = useState<ActivityPage>();
  const [error, setError] = useState<{ key: string; detail?: string }>();
  const [busy, setBusy] = useState<string>();
  const [loading, setLoading] = useState(false);
  const [open, setOpen] = useState<string>();
  const [history, setHistory] = useState<{ engine: "metadata" | "processing"; source?: string }>();
  const request = useRef<AbortController>();
  const load = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();

    request.current = controller;
    setLoading(true);
    try {
      const result = await activityApi.list(filters, controller.signal);

      if (!controller.signal.aborted) {
        setPage(result);
        setError(undefined);
      }
    } catch (cause) {
      if (!controller.signal.aborted)
        setError({
          key: "dataSync.activity.loadFailed",
          detail: cause instanceof Error ? cause.message : undefined,
        });
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    void load();

    return () => request.current?.abort();
  }, [load, version]);
  useEffect(() => {
    const timer = window.setInterval(() => {
      if (!document.hidden && !busy) void load();
    }, 10000);

    return () => window.clearInterval(timer);
  }, [load, busy]);
  const change = (key: keyof ActivityFilters, value: string) => {
    setOpen(undefined);
    setFilters((current) => ({ ...current, [key]: value || undefined, offset: 0 }));
  };
  const act = async (row: SyncActivity, operation: () => Promise<unknown>) => {
    setBusy(row.id);
    try {
      await operation();
      await load();
    } catch (cause) {
      setError({
        key: "dataSync.activity.operationFailed",
        detail: cause instanceof Error ? cause.message : undefined,
      });
    } finally {
      setBusy(undefined);
    }
  };
  const stateText = (state: string) =>
    t(`dataSync.activity.state.${states.includes(state) ? state : "Unknown"}`);
  const phaseText = (row: SyncActivity) =>
    phases.has(row.phase)
      ? t(`dataSync.activity.phase.${row.phase}`)
      : row.engine === "processing" && processingPhases.has(row.phase)
        ? t(`resourceProfile.processing.phase.${row.phase}`)
        : stateText(row.state);
  const devices = page?.devices ?? [];

  return (
    <section
      aria-labelledby="sync-activity-title"
      className={`${panelClass} space-y-3`}
      data-testid="sync-activity-center"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-semibold" id="sync-activity-title">
            {t("dataSync.activity.title")}
          </h2>
          <p className="mt-1 text-xs text-default-500">{t("dataSync.activity.description")}</p>
        </div>
        <Button
          className={smallButtonClass}
          disabled={loading || !!busy}
          size="sm"
          type="button"
          variant="bordered"
          onClick={() => void load()}
        >
          <AiOutlineReload aria-hidden />
          {t("dataSync.refresh")}
        </Button>
      </div>
      <div className="flex flex-wrap gap-2">
        <Select
          disallowEmptySelection
          aria-label={t<string>("dataSync.activity.filter.type")}
          className="w-full sm:w-48"
          dataSource={[
            { value: "", label: t("dataSync.activity.filter.allTypes") },
            ...(page?.dataTypes ?? []).map((kind) => ({
              value: kind,
              label: t(`dataSync.activity.type.${kind}`),
            })),
          ]}
          selectedKeys={[filters.dataType ?? ""]}
          size="sm"
          variant="bordered"
          onChange={(event) => change("dataType", event.target.value)}
        />
        <Select
          disallowEmptySelection
          aria-label={t<string>("dataSync.activity.filter.device")}
          className="w-full sm:w-48"
          dataSource={[
            { value: "", label: t("dataSync.activity.filter.allDevices") },
            ...devices.map((device) => ({ value: device.id, label: device.name })),
          ]}
          selectedKeys={[filters.deviceId ?? ""]}
          size="sm"
          variant="bordered"
          onChange={(event) => change("deviceId", event.target.value)}
        />
        <Select
          disallowEmptySelection
          aria-label={t<string>("dataSync.activity.filter.state")}
          className="w-full sm:w-48"
          dataSource={[
            { value: "", label: t("dataSync.activity.filter.allStates") },
            ...states.map((state) => ({ value: state, label: stateText(state) })),
          ]}
          selectedKeys={[filters.state ?? ""]}
          size="sm"
          variant="bordered"
          onChange={(event) => change("state", event.target.value)}
        />
        <div className="ml-auto flex flex-wrap gap-2">
          <Button
            className={smallButtonClass}
            size="sm"
            type="button"
            variant="bordered"
            onClick={() => setHistory({ engine: "metadata", source: filters.deviceId })}
          >
            {t("dataSync.activity.metadataHistory")}
          </Button>
          <Button
            className={smallButtonClass}
            size="sm"
            type="button"
            variant="bordered"
            onClick={() => setHistory({ engine: "processing" })}
          >
            {t("dataSync.activity.processingHistory")}
          </Button>
        </div>
      </div>
      {error && (
        <div role="alert">
          <ErrorExplanation detail={error.detail} message={t(error.key)} />
        </div>
      )}
      {!page && loading && (
        <p className="py-5 text-center text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      {page && page.items.length === 0 && (
        <p className="py-6 text-center text-sm text-default-500">{t("dataSync.activity.empty")}</p>
      )}
      <div className="divide-y divide-default-100">
        {page?.items.map((row) => (
          <div key={row.id} className="py-2" data-testid="sync-activity-row">
            <button
              aria-expanded={open === row.id}
              className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 rounded-md py-1 text-left text-xs hover:bg-default-50"
              type="button"
              onClick={() => setOpen(open === row.id ? undefined : row.id)}
            >
              {open === row.id ? (
                <AiOutlineDown aria-hidden className="shrink-0" />
              ) : (
                <AiOutlineRight aria-hidden className="shrink-0" />
              )}
              <span className="w-48 shrink-0 font-medium">
                {t(`dataSync.activity.type.${row.dataType}`)}
              </span>
              <span className="flex min-w-0 flex-1 flex-wrap items-center gap-1.5">
                <DeviceDot name={row.source.name} serverId={row.source.id} />
                <span className="max-w-40 truncate">{row.source.name}</span>
                {row.engine !== "processing" && (
                  <>
                    <span aria-hidden>→</span>
                    <DeviceDot name={row.destination.name} serverId={row.destination.id} />
                    <span className="max-w-40 truncate">{row.destination.name}</span>
                  </>
                )}
                {row.engine === "processing" && (
                  <span className="text-default-500">{t("dataSync.activity.resultOwner")}</span>
                )}
              </span>
              {row.itemCount != null && (
                <span className="tabular-nums text-default-500">
                  {t("dataSync.activity.count", { count: row.itemCount })}
                </span>
              )}
              <span
                className={`w-28 shrink-0 ${attention.has(row.state) ? "text-danger" : row.state === "Current" || row.state === "Completed" ? "text-success-700" : "text-default-500"}`}
              >
                {stateText(row.state)}
              </span>
            </button>
            {open === row.id && (
              <div
                className="ml-5 mt-2 space-y-2 rounded-lg bg-default-50 p-3 text-xs"
                data-testid="sync-activity-details"
              >
                <div className="flex flex-wrap gap-x-5 gap-y-1 text-default-500">
                  <span>
                    {t("dataSync.activity.phaseLabel")}: {phaseText(row)}
                  </span>
                  {row.lastSuccessAtUtc && (
                    <span>
                      {t(
                        row.phase === "PeerReport"
                          ? "dataSync.activity.lastReport"
                          : "dataSync.activity.lastSuccess",
                      )}
                      : {localDateTime(row.lastSuccessAtUtc)}
                    </span>
                  )}
                  {row.engine === "processing" && (
                    <span className="flex items-center gap-1">
                      {t("dataSync.activity.worker")}:{" "}
                      {row.worker ? (
                        <>
                          <DeviceDot name={row.worker.name} serverId={row.worker.id} />
                          {row.worker.name}
                        </>
                      ) : (
                        t("dataSync.activity.workerUnknown")
                      )}
                    </span>
                  )}
                  {row.job && (
                    <span>
                      {t("dataSync.activity.role")}: {t(`dataSync.activity.role.${row.job.role}`)}
                    </span>
                  )}
                </div>
                {row.engine === "metadata" && (
                  <p className="text-default-500">
                    {t(
                      row.cachedDataAvailable
                        ? "dataSync.activity.cached"
                        : "dataSync.activity.initial",
                    )}
                  </p>
                )}
                {row.engine === "metadata" && (
                  <p className="text-default-500">{t("dataSync.activity.metadataTogether")}</p>
                )}
                {row.engine === "definitions" && row.pendingDecisions > 0 && (
                  <p>{t("dataSync.activity.pendingDecisions", { count: row.pendingDecisions })}</p>
                )}
                {row.job?.state === "Conflict" && (
                  <p className="text-danger">{t("resourceProfile.processing.conflict")}</p>
                )}
                <ActivityErrorNotice {...row} sourceName={row.source.name} />
                <div className="flex flex-wrap gap-2">
                  {row.engine === "definitions" && row.linkId != null && (
                    <>
                      <Button
                        className={smallButtonClass}
                        disabled={!!busy}
                        size="sm"
                        type="button"
                        variant="bordered"
                        onClick={() => void act(row, () => dataSyncApi.syncNow(row.linkId!))}
                      >
                        {t("dataSync.link.syncNow")}
                      </Button>
                      <Button
                        className={smallButtonClass}
                        size="sm"
                        type="button"
                        variant="bordered"
                        onClick={() =>
                          onReviewDefinitions(
                            row.source.id,
                            row.dataType.slice("definitions.".length),
                          )
                        }
                      >
                        {t("dataSync.activity.review")}
                      </Button>
                      <Button
                        className={smallButtonClass}
                        size="sm"
                        type="button"
                        variant="bordered"
                        onClick={() => onDefinitionHistory(row.source.id)}
                      >
                        {t("dataSync.history.title")}
                      </Button>
                    </>
                  )}
                  {row.engine === "metadata" &&
                    row.libraryEpoch &&
                    !["Denied", "IdentityChanged", "UpdateRequired"].includes(row.state) && (
                      <Button
                        className={smallButtonClass}
                        disabled={!!busy}
                        size="sm"
                        type="button"
                        variant="bordered"
                        onClick={() =>
                          void act(row, () =>
                            activityApi.metadataPause(row, row.state !== "Paused"),
                          )
                        }
                      >
                        {t(
                          row.state === "Paused"
                            ? "management.metadata.resume"
                            : "management.metadata.pause",
                        )}
                      </Button>
                    )}
                  {row.engine === "metadata" && (
                    <Button
                      className={smallButtonClass}
                      size="sm"
                      type="button"
                      variant="bordered"
                      onClick={() => setHistory({ engine: "metadata", source: row.source.id })}
                    >
                      {t("dataSync.activity.runHistory")}
                    </Button>
                  )}
                  {row.job?.role === "Manager" &&
                    processingActions(row.job).map((action) => (
                      <Button
                        key={action}
                        className={smallButtonClass}
                        disabled={!!busy}
                        size="sm"
                        type="button"
                        variant="bordered"
                        onClick={() => void act(row, () => processingApi.control(row.job!, action))}
                      >
                        {t(`resourceProfile.processing.action.${action}`)}
                      </Button>
                    ))}
                </div>
              </div>
            )}
          </div>
        ))}
      </div>
      {page && ((filters.offset ?? 0) > 0 || page.nextOffset != null) && (
        <div className="flex items-center justify-end gap-2 text-xs">
          <span className="text-default-500">
            {(filters.offset ?? 0) + 1}–{(filters.offset ?? 0) + page.items.length} / {page.total}
          </span>
          {(filters.offset ?? 0) > 0 && (
            <Button
              className={smallButtonClass}
              size="sm"
              type="button"
              variant="bordered"
              onClick={() =>
                setFilters((current) => ({
                  ...current,
                  offset: Math.max(0, (current.offset ?? 0) - 50),
                }))
              }
            >
              {t("dataSync.activity.previous")}
            </Button>
          )}
          {page.nextOffset != null && (
            <Button
              className={smallButtonClass}
              size="sm"
              type="button"
              variant="bordered"
              onClick={() => setFilters((current) => ({ ...current, offset: page.nextOffset! }))}
            >
              {t("dataSync.activity.next")}
            </Button>
          )}
        </div>
      )}
      {history && (
        <ActivityHistory
          key={`${history.engine}:${history.source}`}
          {...history}
          onClose={() => setHistory(undefined)}
        />
      )}
    </section>
  );
}

function ActivityHistory({
  engine,
  source,
  onClose,
}: {
  engine: "metadata" | "processing";
  source?: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [entries, setEntries] = useState<(MetadataRun | ProcessingJob)[]>();
  const [cursor, setCursor] = useState<string | number>();
  const [next, setNext] = useState<string | number>();
  const [role, setRole] = useState(0);
  const [error, setError] = useState<{ key: string; detail?: string }>();

  useEffect(() => {
    const abort = new AbortController();

    setEntries(undefined);
    const load = async () => {
      try {
        if (engine === "metadata") {
          const page = await activityApi.metadataHistory(source, cursor as number, abort.signal);

          if (!abort.signal.aborted) {
            setEntries(page.entries);
            setNext(page.nextBeforeId ?? undefined);
          }
        } else {
          const jobs = await activityApi.processingHistory(role, cursor as string, abort.signal);

          if (!abort.signal.aborted) {
            setEntries(jobs);
            setNext(jobs.length === 30 ? jobs.at(-1)?.id : undefined);
          }
        }
        if (!abort.signal.aborted) setError(undefined);
      } catch (cause) {
        if (!abort.signal.aborted)
          setError({
            key: "dataSync.activity.loadFailed",
            detail: cause instanceof Error ? cause.message : undefined,
          });
      }
    };

    void load();

    return () => abort.abort();
  }, [engine, source, cursor, role]);

  return (
    <section
      className="space-y-2 rounded-lg border border-default-200 p-3 text-xs"
      data-testid="sync-activity-history"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">
          {t(
            engine === "metadata"
              ? "dataSync.activity.metadataHistory"
              : "dataSync.activity.processingHistory",
          )}
        </h3>
        {engine === "processing" && (
          <Select
            disallowEmptySelection
            aria-label={t<string>("dataSync.activity.role")}
            className="w-full sm:w-40"
            dataSource={["Manager", "Owner", "Worker"].map((value, index) => ({
              value: String(index),
              label: t(`dataSync.activity.role.${value}`),
            }))}
            selectedKeys={[String(role)]}
            size="sm"
            variant="bordered"
            onChange={(event) => {
              setRole(Number(event.target.value));
              setCursor(undefined);
            }}
          />
        )}
        <Button
          className={smallButtonClass}
          size="sm"
          type="button"
          variant="bordered"
          onClick={onClose}
        >
          {t("dataSync.close")}
        </Button>
      </div>
      {engine === "metadata" && (
        <p className="text-default-500">{t("dataSync.activity.retention")}</p>
      )}
      {error && (
        <div role="alert">
          <ErrorExplanation detail={error.detail} message={t(error.key)} />
        </div>
      )}
      {!entries && !error && <p role="status">{t("dataSync.loading")}</p>}
      {entries?.length === 0 && (
        <p className="py-4 text-center text-default-500">{t("dataSync.history.empty")}</p>
      )}
      <div className="divide-y divide-default-100">
        {entries?.map((entry) => (
          <details key={entry.id} className="py-2">
            <summary className="cursor-pointer">
              <span className="mr-3">{localDateTime(entry.updatedAtUtc)}</span>
              <span className="mr-3">
                {t(
                  `dataSync.activity.state.${[...states, "Checkpointed", "Interrupted"].includes(entry.state) ? entry.state : "Unknown"}`,
                )}
              </span>
              <span className="text-default-500">
                {t("dataSync.activity.processed", { count: entry.processedCount })}
              </span>
            </summary>
            <div className="mt-2 space-y-1 pl-4 text-default-500">
              <p>
                {t("dataSync.activity.phaseLabel")}:{" "}
                {phases.has(entry.phase ?? "")
                  ? t(`dataSync.activity.phase.${entry.phase}`)
                  : processingPhases.has(entry.phase ?? "")
                    ? t(`resourceProfile.processing.phase.${entry.phase}`)
                    : t("dataSync.activity.state.Unknown")}
              </p>
              {"serverId" in entry && (
                <p className="flex items-center gap-1">
                  <DeviceDot name={entry.name ?? entry.serverId} serverId={entry.serverId} />
                  {entry.name ?? entry.serverId} ·{" "}
                  {t("dataSync.activity.count", { count: entry.resourceCount })}
                </p>
              )}
              {"ownerId" in entry && (
                <p>
                  {t("dataSync.activity.resultOwner")}: {entry.ownerId}
                </p>
              )}
              <ActivityErrorNotice {...entry} engine={engine} />
            </div>
          </details>
        ))}
      </div>
      {(cursor !== undefined || next !== undefined) && (
        <div className="flex justify-end gap-2">
          {cursor !== undefined && (
            <Button
              className={smallButtonClass}
              size="sm"
              type="button"
              variant="bordered"
              onClick={() => setCursor(undefined)}
            >
              {t("dataSync.activity.newest")}
            </Button>
          )}
          {next !== undefined && (
            <Button
              className={smallButtonClass}
              size="sm"
              type="button"
              variant="bordered"
              onClick={() => setCursor(next)}
            >
              {t("dataSync.activity.older")}
            </Button>
          )}
        </div>
      )}
    </section>
  );
}
