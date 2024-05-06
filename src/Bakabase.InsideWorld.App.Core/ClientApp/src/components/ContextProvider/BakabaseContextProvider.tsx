import { ConfigProvider, theme } from 'antd';
import { NextUIProvider } from '@nextui-org/react';
import React, { createContext, useContext, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigation } from 'ice';
import store from '@/store';
import { UiTheme } from '@/sdk/constants';
import { uuidv4 } from '@/components/utils';

interface IContext {
  isDarkMode: boolean;
  createPortal: <P>(C: any, props: P) => { remove: () => void };
}

export const BakabaseContext = createContext<IContext>({
  createPortal<P>(C: any, props: P): { remove: () => void } {
    return {
      remove: function () {
      },
    };
  },
  isDarkMode: false,
});

export default ({ children }) => {
  const appOptions = store.useModelState('appOptions');
  const isDarkMode = appOptions.uiTheme == UiTheme.Dark;
  const [componentMap, setComponentMap] = useState<Record<string, { Component: any; props: any }>>({});

  function removePortal(key: string) {
    delete componentMap[key];
    setComponentMap({ ...componentMap });
  }

  function createPortal<P>(C: any, props: P) {
    console.log('creating portal');
    const key = uuidv4();
    componentMap[key] = {
      Component: C,
      props,
    };

    setComponentMap({ ...componentMap });

    return {
      remove: () => removePortal(key),
    };
  }

  useEffect(() => {
    if (appOptions.initialized) {

    }
  }, [appOptions]);

  useEffect(() => {
    console.log('bakabase context provider initialized');
    return () => {
      console.log('bakabase context provider is unmounting');
    };
  }, []);


  return (
    <NextUIProvider>
      <ConfigProvider
        theme={{
          algorithm: isDarkMode ? theme.darkAlgorithm : theme.defaultAlgorithm,
        }}
      >
        <BakabaseContext.Provider
          value={{
            isDarkMode,
            createPortal,
          }}
        >
          {children}
          {Object.keys(componentMap).map(key => {
            const {
              Component,
              props,
            } = componentMap[key];
            return (
              <Component
                {...props}
                onDestroy={() => {
                  props.onDestroy?.();
                  removePortal(key);
                }}
              />
            );
          })}
        </BakabaseContext.Provider>
      </ConfigProvider>
    </NextUIProvider>
  );
};

export const useBakabaseContext = () => {
  return useContext(BakabaseContext);
};
