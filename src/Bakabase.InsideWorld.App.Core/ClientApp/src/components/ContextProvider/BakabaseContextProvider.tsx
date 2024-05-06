import { ConfigProvider, theme } from 'antd';
import { NextUIProvider, useModal } from '@nextui-org/react';
import type { FC } from 'react';
import React, { createContext, useContext, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigation } from 'ice';
import store from '@/store';
import { UiTheme } from '@/sdk/constants';
import { uuidv4 } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';

type CreatePortal = <P extends DestroyableProps>(C: FC<P>, props: P) => { remove: () => void };

interface IContext {
  isDarkMode: boolean;
  createPortal: CreatePortal;
}

export const BakabaseContext = createContext<IContext>({
  createPortal: () => ({ remove: () => {} }),
  isDarkMode: false,
});

export default ({ children }) => {
  const appOptions = store.useModelState('appOptions');
  const isDarkMode = appOptions.uiTheme == UiTheme.Dark;
  const [componentMap, setComponentMap] = useState<Record<string, { Component: any; props: any}>>({});

  function removePortal(key: string) {
    delete componentMap[key];
    setComponentMap({ ...componentMap });
  }

  const createPortal: CreatePortal = (C, props) => {
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
  };

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

  console.log('current components', componentMap);

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
                key={key}
                {...props}
                onDestroyed={() => {
                  if (props.onDestroyed) {
                    props.onDestroyed();
                  }
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
