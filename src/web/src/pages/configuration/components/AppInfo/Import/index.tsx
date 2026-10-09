"use client";

import type { AppDataImportPhase, AppDataImportProgress, AppDataImportStatus } from "./api";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  cancelAppDataRelocation,
  createAppDataRelocationSetupSession,
  getAppDataRelocationStatus,
} from "../Relocation/api";

import {
  cancelAppDataImport,
  createAppDataImportSetupSession,
  getAppDataImportMonitorUrl,
  getAppDataImportSetupUrl,
  getAppDataImportStatus,
  navigateToAppDataSetup,
} from "./api";

import { Button, Modal, toast } from "@/components/bakaui";
import { humanFileSize } from "@/components/utils";

type Operation = "import" | "relocate";
const operationKey = (operation: Operation, suffix: string) =>
  `configuration.${operation === "import" ? "dataImport" : "dataRelocation"}.${suffix}`;
const isActivePhase = (phase?: AppDataImportPhase) =>
  phase != null && !["completed", "failed", "cancelled"].includes(phase);

/** Opens the shared setup flow and summarizes data maintenance already in progress. */
export default function AppDataMaintenanceButton({
  operation = "import",
}: {
  operation?: Operation;
}) {
  const { t, i18n } = useTranslation();
  const key = (suffix: string) => operationKey(operation, suffix);
  const getStatus = operation === "import" ? getAppDataImportStatus : getAppDataRelocationStatus;
  const createSetupSession =
    operation === "import" ? createAppDataImportSetupSession : createAppDataRelocationSetupSession;
  const cancelOperation = operation === "import" ? cancelAppDataImport : cancelAppDataRelocation;
  const [status, setStatus] = useState<AppDataImportStatus>();
  const [visible, setVisible] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [disconnected, setDisconnected] = useState(false);
  const openingSetupRef = useRef(false);
  const mountedRef = useRef(true);
  const previousProgressRef = useRef<AppDataImportProgress>();
  const phase =
    status?.progress?.phase ?? (status?.sourcePath || status?.targetPath ? "queued" : undefined);
  const active = isActivePhase(phase);
  const automaticMaintenance = status?.automaticMaintenance === true;

  useEffect(() => {
    let cancelled = false;

    mountedRef.current = true;
    void getStatus()
      .then((response) => {
        if (!cancelled && !response.code && response.data?.supported) {
          setStatus(response.data);
        }
      })
      .catch(() => {
        // Older backends have no maintenance endpoint. A failed probe must not offer it.
      });

    return () => {
      cancelled = true;
      mountedRef.current = false;
    };
  }, [getStatus]);

  useEffect(() => {
    if (!status?.supported || busy || (!visible && !active)) return;

    let cancelled = false;
    let controller: AbortController | undefined;
    let timeout: ReturnType<typeof setTimeout> | undefined;
    let timer: ReturnType<typeof setTimeout>;

    const refresh = async () => {
      controller = new AbortController();
      timeout = setTimeout(() => controller?.abort(), 10_000);
      try {
        const response = await getStatus(controller.signal);

        if (cancelled) return;
        if (response.code || !response.data?.supported) throw new Error("Status unavailable");
        setStatus(response.data);
        setDisconnected(false);
      } catch {
        // The main API can disappear throughout copying. Keep the last known state;
        // absence of a response never means that the import completed or was cancelled.
        if (!cancelled) setDisconnected(true);
      } finally {
        clearTimeout(timeout);
        if (!cancelled) timer = setTimeout(refresh, 3000);
      }
    };

    timer = setTimeout(refresh, 3000);

    return () => {
      cancelled = true;
      clearTimeout(timer);
      clearTimeout(timeout);
      controller?.abort();
    };
  }, [status?.supported, active, visible, busy, getStatus]);

  useEffect(() => {
    const current = status?.progress;
    const previous = previousProgressRef.current;

    previousProgressRef.current = current;
    if (!current || previous?.id !== current.id || previous.phase === current.phase) return;
    setError(undefined);
    if (current.phase === "completed") {
      toast.success(t(key("completedNotice")));
    } else if (current.phase === "failed") {
      toast.danger({ title: t(key("failedNotice")), description: current.error });
    }
  }, [status?.progress, t, operation]);

  if (!status?.supported) return null;

  const showError = (failure: unknown) => {
    setError(failure instanceof Error ? failure.message : t(key("error")));
  };

  const launchSetup = async () => {
    if (busy || openingSetupRef.current) return;

    openingSetupRef.current = true;
    setBusy(true);
    setError(undefined);
    try {
      const response = await createSetupSession();

      if (!mountedRef.current) return;
      if (response.code || !response.data?.setupToken?.trim()) {
        throw new Error(response.message || t(key("setupFailed")));
      }
      // Issuing a scoped session does not choose a source or queue an import. Those
      // decisions are made once, in the shared first-run / maintenance page.
      navigateToAppDataSetup(
        getAppDataImportSetupUrl(response.data.setupToken, i18n.language, response.data.setupUrl),
      );
    } catch (failure) {
      if (mountedRef.current) {
        showError(failure);
        setVisible(true);
      }
    } finally {
      openingSetupRef.current = false;
      if (mountedRef.current) setBusy(false);
    }
  };

  const cancel = async () => {
    if (busy || disconnected || automaticMaintenance || phase !== "queued") return;

    setBusy(true);
    setError(undefined);
    try {
      const response = await cancelOperation();

      if (response.code) throw new Error(response.message || t(key("error")));
      setStatus({
        ...status,
        sourcePath: undefined,
        targetPath: undefined,
        originalDataPath: undefined,
        progress: status.progress ? { ...status.progress, phase: "cancelled" } : undefined,
      });
      setDisconnected(false);
    } catch (failure) {
      showError(failure);
    } finally {
      setBusy(false);
    }
  };

  const showingProgress = Boolean(phase);
  const sourcePath = status.sourcePath ?? status.progress?.sourcePath;
  const targetPath = status.targetPath ?? status.progress?.targetPath;
  const importsToNewDirectory = operation === "import" && Boolean(targetPath);
  const monitorUrl = status.monitorToken
    ? getAppDataImportMonitorUrl(status.monitorToken, i18n.language, status.monitorUrl)
    : undefined;
  const buttonKey = !showingProgress
    ? "button"
    : phase === "queued" && !automaticMaintenance
      ? "pending"
      : active
        ? "inProgress"
        : phase === "failed"
          ? "failedNotice"
          : "viewResult";

  return (
    <>
      <Button
        color={showingProgress && active ? "warning" : phase === "failed" ? "danger" : "primary"}
        isLoading={busy && !showingProgress}
        size="sm"
        variant="light"
        onPress={() => {
          setError(undefined);
          if (showingProgress) setVisible(true);
          else void launchSetup();
        }}
      >
        {t(key(buttonKey))}
      </Button>
      <Modal
        footer={
          <>
            <Button isDisabled={busy} variant="light" onPress={() => setVisible(false)}>
              {t("Close")}
            </Button>
            {showingProgress ? (
              phase === "queued" && !automaticMaintenance ? (
                <Button
                  color="warning"
                  isDisabled={disconnected}
                  isLoading={busy}
                  onPress={() => void cancel()}
                >
                  {t(key("cancel"))}
                </Button>
              ) : phase === "completed" || phase === "cancelled" ? (
                <Button color="primary" isLoading={busy} onPress={() => void launchSetup()}>
                  {t(key("newImport"))}
                </Button>
              ) : null
            ) : (
              <Button color="primary" isLoading={busy} onPress={() => void launchSetup()}>
                {t(key("openSetup"))}
              </Button>
            )}
          </>
        }
        hideCloseButton={busy}
        isDismissable={!busy}
        isKeyboardDismissDisabled={busy}
        size="lg"
        title={t(
          key(
            showingProgress
              ? phase === "queued" && !automaticMaintenance
                ? "restartTitle"
                : "progressTitle"
              : "button",
          ),
        )}
        visible={visible}
        onClose={() => setVisible(false)}
      >
        <div className="flex flex-col gap-4">
          {showingProgress ? (
            <>
              {status.progress && (
                <ImportProgress
                  automaticMaintenance={automaticMaintenance}
                  importsToNewDirectory={importsToNewDirectory}
                  operation={operation}
                  progress={status.progress}
                />
              )}
              {importsToNewDirectory && (
                <dl className="flex flex-col gap-1 text-sm">
                  <dt className="text-foreground-500">{t(key("sourcePath"))}</dt>
                  <dd>
                    <code className="break-all">{sourcePath}</code>
                  </dd>
                  <dt className="mt-2 text-foreground-500">{t(key("targetPath"))}</dt>
                  <dd>
                    <code className="break-all">{targetPath}</code>
                  </dd>
                </dl>
              )}
              {phase === "queued" && !automaticMaintenance && (
                <>
                  <p>{t(key(importsToNewDirectory ? "restartBodyNewDirectory" : "restartBody"))}</p>
                  {!importsToNewDirectory && (
                    <code className="break-all text-sm">
                      {operation === "import" ? sourcePath : targetPath}
                    </code>
                  )}
                  <p className="text-sm text-foreground-500">{t(key("restartInstructions"))}</p>
                  {operation === "import" && (
                    <code className="rounded bg-default-100 p-2 text-sm">
                      docker compose restart
                    </code>
                  )}
                  <p className="text-sm text-foreground-500">{t(key("keepSource"))}</p>
                </>
              )}
              {automaticMaintenance && (phase === "queued" || phase === "stopping") && (
                <>
                  <p>{t(key(phase === "stopping" ? "automaticStopping" : "automaticQueued"))}</p>
                  {!importsToNewDirectory && (sourcePath || targetPath) && (
                    <code className="break-all text-sm">
                      {operation === "import" ? sourcePath : targetPath}
                    </code>
                  )}
                  <p className="text-sm text-foreground-500">{t(key("keepSource"))}</p>
                </>
              )}
              {automaticMaintenance && phase === "failed" && (
                <p className="text-sm text-foreground-500">{t(key("automaticFailure"))}</p>
              )}
              {disconnected && (
                <p className="text-sm text-warning-600" role="status">
                  {t(key("reconnecting"))}
                </p>
              )}
              {monitorUrl && (
                <div className="flex flex-col gap-2 rounded border border-primary-200 p-3">
                  <a
                    className="font-semibold text-primary underline"
                    href={monitorUrl}
                    rel="noopener noreferrer"
                    target="_blank"
                  >
                    {t(key("openMonitor"))}
                  </a>
                  <p className="text-sm text-foreground-500">
                    {t(key(automaticMaintenance ? "automaticMonitorHelp" : "monitorHelp"))}
                  </p>
                </div>
              )}
            </>
          ) : (
            <p className="text-sm text-foreground-500">{t(key("setupIntro"))}</p>
          )}
          {error && (
            <p className="text-sm text-danger" role="alert">
              {error}
            </p>
          )}
        </div>
      </Modal>
    </>
  );
}

function ImportProgress({
  progress,
  operation,
  importsToNewDirectory,
  automaticMaintenance,
}: {
  progress: AppDataImportProgress;
  operation: Operation;
  importsToNewDirectory: boolean;
  automaticMaintenance: boolean;
}) {
  const { t } = useTranslation();
  const key = (suffix: string) => operationKey(operation, suffix);
  const active = isActivePhase(progress.phase);
  const copying = progress.phase === "copying";
  const moving = progress.phase === "backing-up" || progress.phase === "installing";
  const total = copying ? progress.totalBytes : moving ? progress.totalEntries : 0;
  const completed = copying ? progress.completedBytes : progress.completedEntries;

  return (
    <div className="flex flex-col gap-2" role="status">
      <p className="font-semibold">
        {t(
          key(
            automaticMaintenance && progress.phase === "queued"
              ? "automaticPreparing"
              : `phase.${progress.phase}`,
          ),
        )}
      </p>
      {active && progress.phase !== "queued" && (
        <>
          <progress
            aria-label={t(key("phaseProgress"))}
            className="h-2 w-full accent-primary"
            max={total > 0 ? total : undefined}
            value={total > 0 ? Math.min(completed, total) : undefined}
          />
          <p className="text-sm text-foreground-500">
            {t(key("elapsed"), { seconds: Math.floor(progress.elapsedSeconds) })}
          </p>
        </>
      )}
      {copying && (
        <>
          <p className="text-sm tabular-nums">
            {t(key("filesProgress"), {
              completed: progress.completedFiles,
              total: progress.totalFiles,
            })}
            {" · "}
            {humanFileSize(progress.completedBytes)} / {humanFileSize(progress.totalBytes)}
          </p>
          {progress.bytesPerSecond > 0 && (
            <p className="text-sm text-foreground-500">
              {humanFileSize(progress.bytesPerSecond)}/s
              {progress.remainingSeconds != null &&
                ` · ${t(key("remaining"), { seconds: Math.ceil(progress.remainingSeconds) })}`}
            </p>
          )}
        </>
      )}
      {moving && (
        <p className="text-sm text-foreground-500">
          {t(key("entriesProgress"), {
            completed: progress.completedEntries,
            total: progress.totalEntries,
          })}
        </p>
      )}
      {active && progress.phase !== "queued" && progress.elapsedSeconds >= 60 && (
        <p className="text-sm text-foreground-500">
          {t(key(progress.phase === "stopping" ? "stoppingLongRunning" : "longRunning"))}
        </p>
      )}
      {progress.phase === "completed" && (
        <p className="text-sm text-success-600">{t(key("completedNotice"))}</p>
      )}
      {progress.phase === "failed" && (
        <p className="text-sm text-danger" role="alert">
          {progress.error || t(key("failedNotice"))}
        </p>
      )}
      {progress.backupPath && (
        <div className="flex flex-col gap-1 text-sm">
          <span className="text-foreground-500">
            {t(key(importsToNewDirectory ? "retainedDataPath" : "backupPath"))}
          </span>
          <code className="break-all">{progress.backupPath}</code>
          {importsToNewDirectory && (
            <p className="text-foreground-500">{t(key("retainedDataHelp"))}</p>
          )}
        </div>
      )}
    </div>
  );
}
