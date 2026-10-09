"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { usePrevious } from "react-use";
import {
  CheckCircleOutlined,
  DownloadOutlined,
  ReloadOutlined,
  WarningOutlined,
} from "@ant-design/icons";

import { Button, Chip, Progress, Spinner } from "@/components/bakaui";
import ErrorDetails from "@/components/Error/Details";
import BApi from "@/sdk/BApi";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";

type Version = {
  version?: string | null;
  description?: string | null;
  canUpdate: boolean;
  installedVersionRecognized?: boolean;
};
type Failure = { stage: "discover" | "latest" | "install"; details: string };
const quiet = { showErrorToast: false };

const errorText = (error: unknown): string => {
  if (typeof error === "string") return error;
  if (error instanceof Error) return error.stack || error.message;
  if (error && typeof error === "object") {
    if ("error" in error && error.error) return errorText(error.error);
    if ("message" in error && typeof error.message === "string") return error.message;
  }

  return String(error);
};

const Component = ({ id }: { id: string }) => {
  const { t } = useTranslation();
  const context = useDependentComponentContextsStore((state) => state.contexts).find(
    (c) => c.id === id,
  );
  const [latestVersion, setLatestVersion] = useState<Version>();
  const [checking, setChecking] = useState(true);
  const [installRequested, setInstallRequested] = useState(false);
  const [failure, setFailure] = useState<Failure>();
  const checkingRef = useRef(false);
  const installRef = useRef(false);
  const requestSequence = useRef(0);
  const alive = useRef(true);
  const installing = installRequested || context?.status === DependentComponentStatus.Installing;
  const wasInstalling = usePrevious(installing);

  const check = useCallback(
    async (rediscover = false) => {
      if (checkingRef.current || installRef.current) return;
      const current = () =>
        useDependentComponentContextsStore.getState().contexts.find((c) => c.id === id);

      if (
        current()?.isAvailableOnCurrentPlatform === false ||
        current()?.status === DependentComponentStatus.Installing
      ) {
        setChecking(false);

        return;
      }
      const sequence = ++requestSequence.current;
      const active = () => alive.current && sequence === requestSequence.current;
      let stage: Failure["stage"] = rediscover ? "discover" : "latest";

      checkingRef.current = true;
      setChecking(true);
      setFailure((previous) =>
        !rediscover && previous?.stage === "install" ? previous : undefined,
      );
      try {
        // Discovery clears a previous installation error on the server. Automatic refreshes
        // must preserve that result; only an explicit check should rediscover a known component.
        if (rediscover) {
          const discovered = await BApi.component.discoverDependentComponent(id, quiet);

          if (!active()) return;
          if (discovered.code) throw discovered;
          if (current()?.isAvailableOnCurrentPlatform === false) return;
        }
        stage = "latest";
        const response = await BApi.component.getDependentComponentLatestVersion(id, quiet);

        if (!active()) return;
        if (response.code) throw response;
        setLatestVersion(response.data ?? { canUpdate: false });
      } catch (error) {
        if (active())
          setFailure((previous) =>
            !rediscover && previous?.stage === "install"
              ? previous
              : { stage, details: errorText(error) },
          );
      } finally {
        if (active()) {
          checkingRef.current = false;
          setChecking(false);
        }
      }
    },
    [id],
  );

  useEffect(() => {
    alive.current = true;
    checkingRef.current = false;
    installRef.current = false;
    setInstallRequested(false);
    setLatestVersion(undefined);
    setFailure(undefined);
    const current = useDependentComponentContextsStore.getState().contexts.find((c) => c.id === id);

    void check(current?.status === DependentComponentStatus.NotInstalled && !current.error);

    return () => {
      alive.current = false;
      requestSequence.current++;
    };
  }, [check, id]);

  useEffect(() => {
    // Wait for both the endpoint and the broadcast state to finish. A progress of 100
    // alone is not success, and one refresh covers local and external installations.
    if (!installing && wasInstalling) {
      void check();
    }
  }, [installing, wasInstalling, check]);

  const install = async () => {
    if (
      installRef.current ||
      checkingRef.current ||
      context?.status === DependentComponentStatus.Installing
    )
      return;
    installRef.current = true;
    const sequence = requestSequence.current;
    const active = () => alive.current && sequence === requestSequence.current;

    setInstallRequested(true);
    setFailure(undefined);
    try {
      const response = await BApi.component.installDependentComponent(id, quiet);

      if (response.code) throw response;
    } catch (error) {
      if (active()) setFailure({ stage: "install", details: errorText(error) });
    } finally {
      if (active()) {
        installRef.current = false;
        setInstallRequested(false);
      }
    }
  };

  if (!context) return <Spinner size="sm" />;
  const unsupported = context.isAvailableOnCurrentPlatform === false;
  const installed = context.status === DependentComponentStatus.Installed;
  const busy = checking || installing;
  const availableVersion =
    latestVersion?.version && latestVersion.version !== "N/A" ? latestVersion.version : undefined;
  const canInstall = !unsupported && !!availableVersion && latestVersion?.canUpdate;
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
              onPress={() => void check(true)}
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
                onPress={() => void install()}
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
          <details className="mt-2">
            <summary className="cursor-pointer text-xs text-foreground-600">
              {t("configuration.dependency.errorDetails")}
            </summary>
            <ErrorDetails className="mt-2" text={activeFailure.details} />
          </details>
        </div>
      )}
      <details className="min-w-0 text-xs text-foreground-400">
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
