'use client'

import { DestroyableProps } from '@/components/bakaui/types';
import { createPortal } from '@/components/ContextProvider/helpers';
import { UIHubConnection } from '@/components/SignalR/UIHubConnection';
import { getUiTheme } from '@/components/utils';
import { useAppOptionsStore } from '@/models/options';
import { UiTheme } from '@/sdk/constants';
import { HeroUIProvider, ToastProvider } from '@heroui/react';
import Clarity from '@microsoft/clarity';
import { ConfigProvider, theme } from 'antd';
import { appWithTranslation, useTranslation } from 'next-i18next';
import type { AppProps } from 'next/app';
import { useContext, ComponentType, createContext, useEffect, useRef } from 'react';
import '@/i18n';

type CreatePortal = <P extends DestroyableProps>(C: ComponentType<P>, props: P) => { destroy: () => void; key: string };

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

function Bakabase({ Component, pageProps }: AppProps) {
  const { i18n } = useTranslation();
  const appOptions = useAppOptionsStore().data;
  const isDarkMode = getUiTheme(appOptions) == UiTheme.Dark;
  const isDebugging = false;
  const firstTimeGotAppOptionsRef = useRef(false);

  useEffect(() => {
    console.log('bakabase context provider initialized');
    Clarity.init("r5xlbsu4fl");
    return () => {
      console.log('bakabase context provider is unmounting');
    };
  }, []);

  useEffect(() => {
    if (appOptions) {

      if (!firstTimeGotAppOptionsRef.current) {
        firstTimeGotAppOptionsRef.current = true;
        Clarity.setTag('appVersion', appOptions.version);
        i18n.changeLanguage(appOptions.language);
      }

      const uiTheme = getUiTheme(appOptions);

      // 设置 html class
      const cls = document.documentElement.classList;
      cls.remove('iw-theme-dark', 'iw-theme-light', 'dark', 'light');
      cls.add(`iw-theme-${uiTheme == UiTheme.Dark ? 'dark' : 'light'}`, uiTheme == UiTheme.Dark ? 'dark' : 'light');
    }

  }, [appOptions]);

  // console.log('current components', componentMap);
  // console.log(appOptions, isDarkMode);

  return (
    <>
      <UIHubConnection />
      <HeroUIProvider>
        <ConfigProvider
          theme={{
            algorithm: isDarkMode ? theme.darkAlgorithm : theme.defaultAlgorithm,
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
            <div className={`${isDarkMode ? 'dark' : 'light'} text-foreground bg-background`}>
              <Component {...pageProps} />
            </div>
          </BakabaseContext.Provider>
        </ConfigProvider>
      </HeroUIProvider>
    </>
  );
}

export default appWithTranslation(Bakabase);