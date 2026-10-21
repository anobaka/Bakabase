"use client";

import type { ComponentType, FC, ReactNode } from "react";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseInfrastructuresComponentsConfigurationsAppAppOptions } from "@/sdk/Api";
import type { WindowOptions } from "@/components/Window";

import { HeroUIProvider, Spinner, ToastProvider } from "@heroui/react";
import { ConfigProvider, theme } from "antd";
import {
  useContext,
  createContext,
  useEffect,
  useRef,
  useState,
  useMemo,
  useCallback,
  Fragment,
} from "react";
import { useHref, useLocation, useNavigate } from "react-router-dom";
import dayjs from "dayjs";
import duration from "dayjs/plugin/duration";

import { UIHubConnection } from "@/components/SignalR/UIHubConnection";
import { getUiTheme, uuidv4 } from "@/components/utils";
import { useAppOptionsStore, useUiStyleOptionsStore } from "@/stores/options";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { UiTheme } from "@/sdk/constants";
import { allCssVariableDefinitions } from "@/cons/uiStyleVariables";
import i18n from "@/i18n";
import BApi from "@/sdk/BApi";
import Window from "@/components/Window";
import { ErrorBoundary } from "@/components/Error";
import { toastPresentation } from "@/components/bakaui/components/Toast/configuration";
import { initAnalytics } from "@/services/Analytics";
import {
  ResourceOwnerScopeProvider,
  useResourceOwnerScope,
} from "@/features/management/resourceContext";

dayjs.extend(duration);

type CreatePortal = <P extends DestroyableProps>(
  C: ComponentType<P>,
  props: P,
) => { destroy: () => void; key: string };

type CreateWindow = <P extends {}>(
  C: ComponentType<P>,
  props: P,
  options?: WindowOptions,
) => { destroy: () => void; key: string };

interface IContext {
  isDarkMode: boolean;
  createPortal: CreatePortal;
  createWindow: CreateWindow;
  /** Close all non-persistent modals/portals */
  closeAllModals: () => void;
  isDebugging?: boolean;
}

export const BakabaseContext = createContext<IContext>({
  createPortal: (C, props) => ({ key: "", destroy: () => {} }),
  createWindow: (C, props, options) => ({ key: "", destroy: () => {} }),
  closeAllModals: () => {},
  isDarkMode: false,
  isDebugging: false,
});

export const useBakabaseContext = (): IContext => {
  const context = useContext(BakabaseContext);
  const resourceScope = useResourceOwnerScope();

  // These windows are mounted by the app root, not through a React portal from
  // the caller. Capture the owner here so nested editors keep addressing the
  // same device even after the resource list's device filter changes.
  return useMemo(() => {
    if (!resourceScope) return context;
    const scoped = (Component: ComponentType<any>) => (props: any) => (
      <ResourceOwnerScopeProvider scope={resourceScope}>
        <Component {...props} />
      </ResourceOwnerScopeProvider>
    );

    return {
      ...context,
      createPortal: ((Component, props) =>
        context.createPortal(scoped(Component), props)) as CreatePortal,
      createWindow: ((Component, props, options) =>
        context.createWindow(scoped(Component), props, options)) as CreateWindow,
    };
  }, [context, resourceScope]);
};

// localStorage 工具函数
const getStoredTheme = (): UiTheme | undefined => {
  // Check if we're in browser environment
  if (typeof window === "undefined" || typeof localStorage === "undefined") {
    return undefined;
  }

  try {
    const stored = localStorage.getItem("bakabase-theme");

    if (stored) {
      const themeValue = parseInt(stored);

      if (Object.values(UiTheme).includes(themeValue)) {
        return themeValue as UiTheme;
      }
    }
  } catch (error) {
    console.warn("Failed to read theme from localStorage:", error);
  }
};

const setStoredTheme = (theme: UiTheme): void => {
  // Check if we're in browser environment
  if (typeof window === "undefined" || typeof localStorage === "undefined") {
    return;
  }

  try {
    localStorage.setItem("bakabase-theme", theme.toString());
  } catch (error) {
    console.warn("Failed to save theme to localStorage:", error);
  }
};

const changeTheme = (theme: UiTheme) => {
  console.log(theme, document);

  // Check if we're in browser environment
  if (typeof document === "undefined") {
    return;
  }

  const cls = document.documentElement.classList;

  cls.remove("iw-theme-dark", "iw-theme-light", "dark", "light");
  cls.add(
    `iw-theme-${theme == UiTheme.Dark ? "dark" : "light"}`,
    theme == UiTheme.Dark ? "dark" : "light",
  );
};

const BakabaseContextProvider: FC<{ children: ReactNode }> = ({ children }) => {
  const [appOptions, setAppOptions] =
    useState<BakabaseInfrastructuresComponentsConfigurationsAppAppOptions>();

  const currentTheme = useRef(getUiTheme(appOptions ? appOptions.uiTheme : getStoredTheme()));
  const isDarkMode = useMemo(() => currentTheme.current === UiTheme.Dark, [currentTheme]);

  const [portals, setPortals] = useState<
    { key: string; component: React.ReactNode; persistent?: boolean }[]
  >([]);

  // Stable callbacks so that the context value reference stays stable across
  // setPortals updates — otherwise every createPortal call would re-render
  // every useBakabaseContext() consumer (including all visible ResourceCards).
  const mount = useCallback((C: ComponentType<any>, props: any) => {
    console.log("[createPortal] mount", C, props);

    const key = uuidv4();

    const destroy = () => {
      console.log("[createPortal] destroy", key);
      setPortals((prev) => prev.filter((p) => p.key !== key));
    };

    const component = (
      <C
        {...props}
        onDestroyed={() => {
          props.onDestroyed?.();
          destroy();
        }}
      />
    );

    setPortals((prev) => [...prev, { key, component, persistent: false }]);

    return { key, destroy };
  }, []);

  const mountWindow = useCallback((C: ComponentType<any>, props: any, options?: WindowOptions) => {
    console.log("[createWindow] mount", C, props, options);

    const key = uuidv4();
    const { title, persistent, renderHeaderActions, ...windowOptions } = options || {};

    const destroy = () => {
      console.log("[createWindow] destroy", key);
      setPortals((prev) => prev.filter((p) => p.key !== key));
    };

    const windowComponent = (
      <Window
        renderHeaderActions={renderHeaderActions}
        title={title}
        windowOptions={windowOptions}
        onDestroyed={() => {
          destroy();
        }}
      >
        <C {...props} />
      </Window>
    );

    setPortals((prev) => [...prev, { key, component: windowComponent, persistent }]);

    return { key, destroy };
  }, []);

  const closeAllModals = useCallback(() => {
    console.log("[closeAllModals] closing all non-persistent modals");
    setPortals((prev) => prev.filter((p) => p.persistent));
  }, []);

  const appOptionsStore = useAppOptionsStore();
  const analyticsContextState = useRemoteAccessStore((state) => state.context);
  const analyticsServerId = useRemoteAccessStore((state) => state.serverId);
  const analyticsClientMode = useRemoteAccessStore((state) => state.clientMode);
  const cssVariableOverwrites = useUiStyleOptionsStore((s) => s.data?.cssVariableOverwrites);
  const isDebugging = false;
  const firstTimeGotAppOptionsRef = useRef(false);

  const location = useLocation();
  const navigate = useNavigate();
  const href = useHref;

  useEffect(() => {
    changeTheme(currentTheme.current);

    BApi.options.getAppOptions().then((r) => {
      setAppOptions(r.data);
    });

    // Ask the server which side of the remote-access gate this browser is on, so
    // actions that run on the host (launching a player, opening a folder) can be
    // replaced rather than silently executed on the wrong machine.
    useRemoteAccessStore.getState().load();

    // A long-lived page keeps its address candidate fresh, and a returning window
    // can recover from a failed startup observation without polling the network.
    const refreshRemoteContext = () => {
      if (!document.hidden) {
        void useRemoteAccessStore.getState().load();
        const remote = useRemoteAccessStore.getState();
        const options = useAppOptionsStore.getState();
        if (options.initialized && remote.context === "known") {
          void initAnalytics({
            enabled: options.data.enableAnonymousDataTracking,
            contextKey: remote.serverId,
            clientMode: remote.clientMode,
            refresh: true,
          });
        }
      }
    };
    const remoteContextTimer = setInterval(refreshRemoteContext, 60 * 60_000);

    window.addEventListener("focus", refreshRemoteContext);

    console.log("bakabase context provider initialized");
    // Analytics SDKs init in the appOptions effect below — they need the toggle value
    // and (via initAnalytics) deployment context / project IDs returned by /app/analytics-info.

    return () => {
      clearInterval(remoteContextTimer);
      window.removeEventListener("focus", refreshRemoteContext);
      console.log("bakabase context provider is unmounting");
    };
  }, []);

  useEffect(() => {
    if (appOptionsStore.initialized) {
      setAppOptions(appOptionsStore.data);
    }
  }, [appOptionsStore]);

  useEffect(() => {
    if (!appOptions) return;
    // Revocation is immediate even if context is still loading or unavailable.
    if (!appOptions.enableAnonymousDataTracking || analyticsContextState === "known") {
      void initAnalytics({
        enabled: appOptions.enableAnonymousDataTracking,
        contextKey: analyticsServerId,
        clientMode: analyticsClientMode,
      });
    }
  }, [
    appOptions?.enableAnonymousDataTracking,
    analyticsContextState,
    analyticsServerId,
    analyticsClientMode,
  ]);

  useEffect(() => {
    if (appOptions) {
      console.log("appOptions", appOptions, firstTimeGotAppOptionsRef.current);
      if (!firstTimeGotAppOptionsRef.current) {
        firstTimeGotAppOptionsRef.current = true;
        i18n.changeLanguage(appOptions.language);
      }

      // 如果有保存的主题，使用保存的主题；否则使用appOptions中的主题
      const uiTheme = getUiTheme(appOptions.uiTheme);

      // console.log(uiTheme, currentTheme.current)

      if (currentTheme.current != uiTheme) {
        currentTheme.current = uiTheme;
        changeTheme(uiTheme);
        // 保存主题到localStorage
        setStoredTheme(uiTheme);
        console.log("theme changed to:", uiTheme);
      }
    }
  }, [appOptions]);

  // Inject CSS variable overwrites to :root
  useEffect(() => {
    const root = document.documentElement;

    for (const def of allCssVariableDefinitions) {
      const value = cssVariableOverwrites?.[def.name] ?? def.defaultValue;

      root.style.setProperty(def.name, value);
    }
  }, [cssVariableOverwrites]);

  // Clear non-persistent portals on route change
  // Persistent windows (created with createWindow) will remain
  useEffect(() => {
    setPortals((prev) => prev.filter((p) => p.persistent));
  }, [location.pathname]);

  console.log("current theme", UiTheme[currentTheme.current], "portals", portals);

  const contextValue = useMemo<IContext>(
    () => ({
      isDarkMode,
      createPortal: mount,
      createWindow: mountWindow,
      closeAllModals,
      isDebugging,
    }),
    [isDarkMode, mount, mountWindow, closeAllModals, isDebugging],
  );

  return (
    <>
      <UIHubConnection />
      <HeroUIProvider navigate={navigate} useHref={href}>
        <ConfigProvider
          theme={{
            algorithm: isDarkMode ? theme.darkAlgorithm : theme.defaultAlgorithm,
          }}
        >
          <ToastProvider placement={"top-center"} toastProps={toastPresentation} />
          <BakabaseContext.Provider value={contextValue}>
            {portals.map(({ key, component }) => (
              <ErrorBoundary key={key}>
                <Fragment>{component}</Fragment>
              </ErrorBoundary>
            ))}
            <div
              className={`${isDarkMode ? "dark" : "light"} h-[100vh] w-[100vw] text-foreground bg-background`}
            >
              {appOptions ? (
                children
              ) : (
                <div className="w-full h-full flex items-center justify-center">
                  <Spinner />
                </div>
              )}
            </div>
          </BakabaseContext.Provider>
        </ConfigProvider>
      </HeroUIProvider>
    </>
  );
};

export default BakabaseContextProvider;
