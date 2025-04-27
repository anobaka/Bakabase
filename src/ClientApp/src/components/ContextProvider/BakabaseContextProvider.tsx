import { ConfigProvider, theme } from 'antd';
import { HeroUIProvider, useModal } from '@heroui/react';
import type { ComponentType, FC } from 'react';
import React, { createContext, useContext, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigation } from 'ice';
import { createPortal } from './helpers';
import store from '@/store';
import { UiTheme } from '@/sdk/constants';
import { getUiTheme, uuidv4 } from '@/components/utils';
import type { DestroyableProps } from '@/components/bakaui/types';

type CreatePortal = <P extends DestroyableProps>(C: ComponentType<P>, props: P) => {destroy: () => void; key: string} ;

interface IContext {
  isDarkMode: boolean;
  createPortal: CreatePortal;
}

export const BakabaseContext = createContext<IContext>({
  createPortal,
  isDarkMode: false,
});

export default ({ children }) => {
  const appOptions = store.useModelState('appOptions');
  // console.log(appOptions);
  const isDarkMode = getUiTheme(appOptions) == UiTheme.Dark;
  // const [componentMap, setComponentMap] = useState<Record<string, { Component: any; props: any}>>({});

  // function removePortal(key: string) {
  //   delete componentMap[key];
  //   setComponentMap({ ...componentMap });
  // }

  // const createPortal: CreatePortal = (C, props) => {
  //   console.log('creating portal');
  //   const key = uuidv4();
  //   componentMap[key] = {
  //     Component: C,
  //     props,
  //   };
  //
  //   setComponentMap({ ...componentMap });
  //
  //   return {
  //     remove: () => removePortal(key),
  //   };
  // };

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

  // console.log('current components', componentMap);
  // console.log(appOptions, isDarkMode);

  return (
    <HeroUIProvider>
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
          <div className={`${isDarkMode ? 'dark' : 'light'} text-foreground bg-background`}>
            {children}
          </div>
          {/* {Object.keys(componentMap).map(key => { */}
          {/*   const { */}
          {/*     Component, */}
          {/*     props, */}
          {/*   } = componentMap[key]; */}
          {/*   return ( */}
          {/*     <Component */}
          {/*       key={key} */}
          {/*       {...props} */}
          {/*       onDestroyed={() => { */}
          {/*         if (props.onDestroyed) { */}
          {/*           props.onDestroyed(); */}
          {/*         } */}
          {/*         removePortal(key); */}
          {/*       }} */}
          {/*     /> */}
          {/*   ); */}
          {/* })} */}
        </BakabaseContext.Provider>
      </ConfigProvider>
    </HeroUIProvider>
  );
};

export const useBakabaseContext = () => {
  return useContext(BakabaseContext);
};
