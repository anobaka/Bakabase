"use client";

import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import {
  CheckCircleOutlined,
  DownloadOutlined,
  ReloadOutlined,
  WarningOutlined,
} from "@ant-design/icons";

import { useDependencyChecks } from "./dependencyChecks";

import { Button, Chip, Progress, Spinner } from "@/components/bakaui";
import ErrorDetails from "@/components/Error/Details";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";
import { dependencyDisplayName, PythonRuntimeId } from "./presentation";

export { useDependencyChecks } from "./dependencyChecks";
export type {
  DependencyCheckEntry,
  DependencyCheckEntries,
  DependencyVersion,
  DependencyFailure,
} from "./dependencyChecks";

const Component = ({
  id,
  mode = "full",
  onDetails,
}: {
  id: string;
  mode?: "full" | "summary" | "details";
  onDetails?: () => void;
}) => {
  const { t } = useTranslation();
  const context = useDependentComponentContextsStore((state) => state.contexts).find(
    (c) => c.id === id,
  );
  const { entries, checkDependency, installDependency } = useDependencyChecks();
  const entry = entries[id];
  const latestVersion = entry?.latestVersion;
  const checking = entry?.checking ?? false;
  const installRequested = entry?.installRequested ?? false;
  const failure = entry?.failure;
  const installing = installRequested || context?.status === DependentComponentStatus.Installing;

  useEffect(() => {
    const current = useDependentComponentContextsStore.getState().contexts.find((c) => c.id === id);

    void checkDependency(
      id,
      current?.status === DependentComponentStatus.NotInstalled &&
        !current.error &&
        failure?.stage !== "install",
    );
  }, [checkDependency, id]);

  if (!context) return <Spinner size="sm" />;
  const unsupported = context.isAvailableOnCurrentPlatform === false;
  const installed = context.status === DependentComponentStatus.Installed;
  const busy = checking || installing;
  const availableVersion =
    latestVersion?.version && latestVersion.version !== "N/A" ? latestVersion.version : undefined;
  const canAutoInstall = context.canAutoInstallOnCurrentPlatform !== false;
  const manualInstallationKey =
    id === PythonRuntimeId ? "dependencies.python.userManaged" : "dependencies.manualInstallation";
  const canInstall =
    !unsupported && canAutoInstall && !!availableVersion && latestVersion?.canUpdate;
  const activeFailure =
    failure?.stage === "install"
      ? failure
      : !installing && context.error
        ? { stage: "install" as const, details: context.error }
        : failure;
  const progress = Math.min(
    100,
    Math.max(0, Number.isFinite(context.installationProgress) ? context.installationProgress : 0),
  );
  const upToDate =
    installed &&
    !busy &&
    !activeFailure &&
    !!availableVersion &&
    !latestVersion?.canUpdate &&
    latestVersion?.installedVersionRecognized !== false;
  const statusKey = unsupported
    ? "notAvailableOnCurrentPlatform"
    : installing
      ? "installing"
      : checking
        ? "checkingVersion"
        : upToDate
          ? "upToDate"
          : installed
            ? "installed"
            : "notInstalled";
  const showLookupUnknown =
    !unsupported && !busy && !activeFailure && latestVersion && !availableVersion;
  const showVersionUnknown =
    installed &&
    !busy &&
    !activeFailure &&
    availableVersion &&
    latestVersion?.installedVersionRecognized === false;
  const failureKey =
    activeFailure?.stage === "install"
      ? "installFailed"
      : activeFailure?.stage === "discover"
        ? "discoveryFailed"
        : "failedToGetVersion";

  if (mode === "summary") {
    return (
      <div aria-busy={busy} className="flex min-w-0 flex-1 flex-col gap-2">
        <div className="flex h-7 items-center gap-2 overflow-hidden">
          <Chip
            color={
              activeFailure ? "danger" : installing ? "primary" : installed ? "success" : "default"
            }
            radius="sm"
            size="sm"
            variant="flat"
            className="max-w-full"
          >
            <span
              aria-label={t(`configuration.dependency.${statusKey}`)}
              className="inline-flex min-w-0 items-center gap-1.5 truncate"
            >
              {upToDate && <CheckCircleOutlined aria-hidden />}
              {t(`configuration.dependency.${statusKey}`)}
            </span>
          </Chip>
          {busy && !unsupported && <Spinner size="sm" />}
        </div>
        <dl className="grid grid-cols-2 gap-2 text-xs">
          <div className="min-w-0">
            <dt className="truncate text-foreground-400">
              {t("configuration.dependency.currentVersion")}
            </dt>
            <dd
              className="mt-0.5 truncate font-mono text-foreground-700"
              title={context.version ?? undefined}
            >
              {context.version || "—"}
            </dd>
          </div>
          <div className="min-w-0">
            <dt className="truncate text-foreground-400">
              {t("configuration.dependency.availableVersion")}
            </dt>
            <dd className="mt-0.5 truncate font-mono text-foreground-700" title={availableVersion}>
              {availableVersion || "—"}
            </dd>
          </div>
        </dl>
        <div className="h-10 min-w-0 text-xs">
          {installing && !unsupported ? (
            <div role="status" className="space-y-1">
              <div className="flex justify-between gap-2">
                <span className="truncate text-foreground-500">
                  {t(
                    progress === 0
                      ? "configuration.dependency.preparing"
                      : progress < 90
                        ? "configuration.dependency.downloading"
                        : "configuration.dependency.installingAndVerifying",
                  )}
                </span>
                <span className="tabular-nums">{progress}%</span>
              </div>
              <Progress
                aria-label={t("configuration.dependency.installationProgress")}
                isIndeterminate={progress === 0}
                size="sm"
                value={progress}
              />
            </div>
          ) : activeFailure && !unsupported ? (
            <p role="alert" className="line-clamp-2 text-danger">
              {t(`configuration.dependency.${failureKey}`)} · {t("dependencies.detailsForError")}
            </p>
          ) : !unsupported && !canAutoInstall ? (
            <p className="line-clamp-2 text-warning-600">{t(manualInstallationKey)}</p>
          ) : showVersionUnknown ? (
            <p className="line-clamp-2 text-warning-600">
              {t("configuration.dependency.installedVersionNotRecognized")}
            </p>
          ) : showLookupUnknown ? (
            <p className="line-clamp-2 text-foreground-500">
              {t("configuration.dependency.couldNotCheckForUpdates")}
            </p>
          ) : null}
        </div>
        <div className="mt-auto flex flex-wrap items-center gap-1.5 pt-1">
          {!unsupported && (
            <Button
              isDisabled={busy}
              size="sm"
              variant="flat"
              onPress={() => void checkDependency(id, true, true)}
            >
              {t("dependencies.action.check")}
            </Button>
          )}
          {canInstall && (
            <Button
              color="primary"
              isDisabled={busy}
              isLoading={installRequested}
              size="sm"
              onPress={() => void installDependency(id)}
            >
              {t(
                installed || context.version
                  ? "configuration.dependency.update"
                  : "configuration.dependency.install",
              )}
            </Button>
          )}
          {onDetails && (
            <Button
              className="ml-auto"
              size="sm"
              variant="light"
              onPress={onDetails}
              aria-haspopup="dialog"
              aria-label={t("dependencies.detailsForComponent", {
                name: dependencyDisplayName(context, t),
              })}
            >
              {t("dependencies.action.details")}
            </Button>
          )}
        </div>
      </div>
    );
  }

  return (
    <div aria-busy={busy} className="flex min-w-0 flex-col gap-3 py-1">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <Chip
            color={installing ? "primary" : installed ? "success" : "default"}
            radius="sm"
            size="sm"
            variant="flat"
          >
            <span
              aria-label={t(`configuration.dependency.${statusKey}`)}
              className="inline-flex items-center gap-1.5"
            >
              {upToDate && <CheckCircleOutlined aria-hidden />}
              {t(`configuration.dependency.${statusKey}`)}
            </span>
          </Chip>
          {busy && !unsupported && <Spinner size="sm" />}
          {showLookupUnknown && (
            <span
              className="text-xs text-foreground-500"
              title={latestVersion.description ?? undefined}
            >
              {t("configuration.dependency.couldNotCheckForUpdates")}
            </span>
          )}
          {showVersionUnknown && (
            <span className="text-xs text-warning">
              {t("configuration.dependency.installedVersionNotRecognized")}
            </span>
          )}
        </div>
        {!unsupported && (
          <div className="flex shrink-0 items-center gap-2">
            <Button
              isDisabled={busy}
              size="sm"
              startContent={<ReloadOutlined aria-hidden />}
              variant="light"
              onPress={() => void checkDependency(id, true, true)}
            >
              {t("configuration.dependency.checkAgain")}
            </Button>
            {canInstall && (
              <Button
                color="primary"
                isDisabled={busy}
                isLoading={installRequested}
                size="sm"
                startContent={!installRequested && <DownloadOutlined aria-hidden />}
                onPress={() => void installDependency(id)}
              >
                {t(
                  installed || context.version
                    ? "configuration.dependency.update"
                    : "configuration.dependency.install",
                )}
              </Button>
            )}
          </div>
        )}
      </div>
      {!unsupported && !canAutoInstall && (
        <p className="text-xs text-warning-600">{t(manualInstallationKey)}</p>
      )}
      {!unsupported && (
        <div className="flex flex-wrap gap-x-6 gap-y-1 text-xs">
          <div className="flex items-baseline gap-2">
            <span className="text-foreground-400">
              {t("configuration.dependency.currentVersion")}
            </span>
            <span className="break-all font-mono text-foreground-700">
              {context.version ||
                t(
                  installed
                    ? "configuration.dependency.versionUnknown"
                    : "configuration.dependency.notInstalled",
                )}
            </span>
          </div>
          <div className="flex items-baseline gap-2">
            <span className="text-foreground-400">
              {t("configuration.dependency.availableVersion")}
            </span>
            <span className="break-all font-mono text-foreground-700">
              {availableVersion || "—"}
            </span>
          </div>
        </div>
      )}
      {installing && !unsupported && (
        <div className="rounded-lg bg-primary/5 p-3">
          <div
            className="mb-2 flex justify-between gap-3 text-xs text-foreground-600"
            role="status"
          >
            <span>
              {t(
                progress === 0
                  ? "configuration.dependency.preparing"
                  : progress < 90
                    ? "configuration.dependency.downloading"
                    : "configuration.dependency.installingAndVerifying",
              )}
            </span>
            <span className="font-mono tabular-nums">{progress}%</span>
          </div>
          <Progress
            aria-label={t("configuration.dependency.installationProgress")}
            isIndeterminate={progress === 0}
            size="sm"
            value={progress}
          />
        </div>
      )}
      {activeFailure && !unsupported && (
        <div className="min-w-0 rounded-lg border border-danger/20 bg-danger/5 p-3">
          <div className="flex items-center gap-2 text-sm text-danger" role="alert">
            <WarningOutlined aria-hidden />
            <span>{t(`configuration.dependency.${failureKey}`)}</span>
          </div>
          <p className="mt-1 line-clamp-2 break-words text-xs text-foreground-500">
            {activeFailure.details
              .split(/\r?\n/)
              .find((line) => line.trim())
              ?.slice(0, 180)}
          </p>
          <details className="mt-2" open={mode === "details"}>
            <summary className="cursor-pointer text-xs text-foreground-600">
              {t("configuration.dependency.errorDetails")}
            </summary>
            <ErrorDetails className="mt-2" text={activeFailure.details} />
          </details>
        </div>
      )}
      {latestVersion?.description?.trim() && (
        <details className="min-w-0 text-xs text-foreground-400" open={mode === "details"}>
          <summary className="w-fit cursor-pointer">{t("dependencies.versionDetails")}</summary>
          <p className="mt-2 whitespace-pre-wrap break-words select-text text-foreground-600">
            {latestVersion.description}
          </p>
        </details>
      )}
      <details className="min-w-0 text-xs text-foreground-400" open={mode === "details"}>
        <summary className="w-fit cursor-pointer">
          {t("configuration.dependency.installationLocation")}
        </summary>
        <p className="mt-1 break-all select-text font-mono text-foreground-600">
          {context.location || context.defaultLocation}
        </p>
      </details>
    </div>
  );
};

export default Component;
