"use client";

import { ConfigProvider, theme } from 'antd';
import { HeroUIProvider, ToastProvider, useModal } from '@heroui/react';
import type { ComponentType, ReactNode, FC } from 'react';
import React, { createContext, useContext, useEffect, useRef, useState } from 'react';
import { createPortal } from './helpers';
import { UiTheme } from '@/sdk/constants';
import { getUiTheme } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';
import { useAppOptionsStore } from "@/models/options";
import { UIHubConnection } from '../SignalR/UIHubConnection';
import Clarity from '@microsoft/clarity';


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


const BakabaseContextProvider: FC<{ children: ReactNode }> = ({ children }) => {
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
              {children}
            </div>
          </BakabaseContext.Provider>
        </ConfigProvider>
      </HeroUIProvider>
    </>
  );
};

export default BakabaseContextProvider;
