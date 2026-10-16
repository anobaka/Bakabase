import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { AiOutlineLoading3Quarters } from "react-icons/ai";

import { deviceSwitchDestination } from "./deviceSwitchDestination";
import { deviceSwitchPath } from "./deviceSwitchRoute";

import { asideMenuConfig } from "@/layouts/BasicLayout/components/PageNav/components/AntdMenu/menuConfig";
import BApi from "@/sdk/BApi";
import { ClientMode } from "@/sdk/constants";
import i18n from "@/i18n";

export const DEVICE_SWITCH_TIMEOUT_MS = 10_000;

function ResolveDestination({ requestedPath }: { requestedPath: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [attempt, setAttempt] = useState(0);
  const [target, setTarget] = useState<string>();
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    const timer = setTimeout(() => {
      controller.abort();
      if (active) setFailed(true);
    }, DEVICE_SWITCH_TIMEOUT_MS);
    const params = { signal: controller.signal, showErrorToast: false };

    void Promise.all([
      BApi.remoteAccess.getRemoteAccessContext(params),
      BApi.options.getUiOptions(params),
      BApi.options.getAppOptions(params),
    ])
      .then(([context, options, app]) => {
        if (!active || controller.signal.aborted) return;
        const data = context.data;

        // An unanswered context is not the default all-in-one capability. Likewise,
        // a failed options read cannot be treated as an empty custom menu layout.
        if (
          context.code ||
          options.code ||
          app.code ||
          !data ||
          typeof data.isLocal !== "boolean" ||
          ![ClientMode.AllInOne, ClientMode.RemoteBrowser, ClientMode.PureClient].includes(
            data.clientMode,
          ) ||
          data.serverReachable === false ||
          !options.data ||
          typeof options.data !== "object" ||
          Array.isArray(options.data) ||
          typeof app.data?.language !== "string" ||
          !app.data.language.trim()
        )
          throw new Error("Could not read the destination navigation context");

        const isPureClient = data.clientMode === ClientMode.PureClient;
        // The normal provider applies this language after startup. Resolve directory
        // names with that same language without mounting it or changing global state.
        const translate = i18n.getFixedT(app.data!.language);

        setTarget(
          deviceSwitchDestination(
            requestedPath,
            asideMenuConfig,
            options.data.navigationLayout,
            {
              isPureClient,
              isLocalNode: !isPureClient && (data.federationAvailable ?? data.isLocal),
            },
            (key) => translate<string>(key),
          ),
        );
      })
      .catch(() => {
        if (active) setFailed(true);
        controller.abort();
      })
      .finally(() => clearTimeout(timer));

    return () => {
      active = false;
      clearTimeout(timer);
      controller.abort();
    };
  }, [requestedPath, attempt]);

  if (target !== undefined) return <Navigate replace to={target} />;

  const buttonClass =
    "rounded-lg border border-default-300 px-3 py-2 text-sm outline-none transition hover:bg-default-100 focus-visible:ring-2 focus-visible:ring-primary/50";

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-6 text-foreground">
      <div className="flex max-w-md flex-col items-center gap-4 text-center">
        {failed ? (
          <>
            <p role="alert">{t("federation.switcher.destinationFailed")}</p>
            <div className="flex gap-2">
              <button
                className={buttonClass}
                type="button"
                onClick={() => {
                  setFailed(false);
                  setAttempt((value) => value + 1);
                }}
              >
                {t("federation.retry")}
              </button>
              <button
                className={buttonClass}
                type="button"
                onClick={() => navigate("/", { replace: true })}
              >
                {t("federation.switcher.goHome")}
              </button>
            </div>
          </>
        ) : (
          <div className="flex items-center gap-2 text-sm text-default-500" role="status">
            <AiOutlineLoading3Quarters aria-hidden className="animate-spin" />
            {t("federation.switcher.resolvingDestination")}
          </div>
        )}
      </div>
    </main>
  );
}

/** Lazy, blank route: no resource page or business provider mounts before resolution. */
export default function DeviceSwitchPage() {
  const { search } = useLocation();
  const paths = new URLSearchParams(search).getAll("path");
  const requestedPath = deviceSwitchPath(paths.length === 1 ? paths[0] : undefined);

  return <ResolveDestination key={search} requestedPath={requestedPath} />;
}
