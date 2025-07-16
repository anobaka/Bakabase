"use client";

import type { ComponentType, FC, ReactNode } from "react";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseInfrastructuresComponentsConfigurationsAppAppOptions } from "@/sdk/Api";

import { HeroUIProvider, Spinner, ToastProvider } from "@heroui/react";
import Clarity from "@microsoft/clarity";
import { ConfigProvider, theme } from "antd";
import {
  useContext,
  createContext,
  useEffect,
  useRef,
  useState,
  useMemo,
} from "react";
import { useHref, useNavigate } from "react-router-dom";
import dayjs from "dayjs";
import duration from "dayjs/plugin/duration";

import { createPortal } from "@/components/ContextProvider/helpers";
import { UIHubConnection } from "@/components/SignalR/UIHubConnection";
import { getUiTheme } from "@/components/utils";
import { useAppOptionsStore } from "@/models/options";
import { UiTheme } from "@/sdk/constants";
import i18n from "@/i18n";
import BApi from "@/sdk/BApi";

dayjs.extend(duration);

type CreatePortal = <P extends DestroyableProps>(
  C: ComponentType<P>,
  props: P,
) => { destroy: () => void; key: string };

interface IContext {
  isDarkMode: boolean;
  createPortal: CreatePortal;
  isDebugging?: boolean;
}

const BakabaseContext = createContext<IContext>({
  createPortal,
  isDarkMode: false,
  isDebugging: false,
});

export const useBakabaseContext = (): IContext => {
  return useContext(BakabaseContext);
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

  const currentTheme = useRef(
    getUiTheme(appOptions ? appOptions.uiTheme : getStoredTheme()),
  );
  const isDarkMode = useMemo(
    () => currentTheme.current === UiTheme.Dark,
    [currentTheme],
  );

  const appOptionsStore = useAppOptionsStore();
  const isDebugging = false;
  const firstTimeGotAppOptionsRef = useRef(false);

  const navigate = useNavigate();
  const href = useHref;

  useEffect(() => {
    changeTheme(currentTheme.current);

    BApi.options.getAppOptions().then((r) => {
      setAppOptions(r.data);
    });

    console.log("bakabase context provider initialized");
    Clarity.init("r5xlbsu4fl");

    return () => {
      console.log("bakabase context provider is unmounting");
    };
  }, []);

  useEffect(() => {
    if (appOptionsStore.initialized) {
      setAppOptions(appOptionsStore.data);
    }
  }, [appOptionsStore]);

  useEffect(() => {
    if (appOptions) {
      console.log("appOptions", appOptions, firstTimeGotAppOptionsRef.current);
      if (!firstTimeGotAppOptionsRef.current) {
        firstTimeGotAppOptionsRef.current = true;
        Clarity.setTag("appVersion", appOptions.version);
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

  console.log(
    "current theme",
    UiTheme[currentTheme.current],
    // "is dark mode",
    // isDarkMode,
  );

  return (
    <>
      <UIHubConnection />
      <HeroUIProvider navigate={navigate} useHref={href}>
        <ConfigProvider
          theme={{
            algorithm: isDarkMode
              ? theme.darkAlgorithm
              : theme.defaultAlgorithm,
          }}
        >
          <ToastProvider />
          <BakabaseContext.Provider
            value={{
              isDarkMode,
              createPortal,
              isDebugging,
            }}
          >
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
