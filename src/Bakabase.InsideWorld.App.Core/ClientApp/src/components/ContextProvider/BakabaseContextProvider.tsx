import { ConfigProvider, theme } from 'antd';
import { NextUIProvider } from '@nextui-org/react';
import React, { createContext, useEffect } from 'react';
import store from '@/store';
import { UiTheme } from '@/sdk/constants';

export const BakabaseContext = createContext({
  isDarkMode: false,
});


export default ({ children }) => {
  const appOptions = store.useModelState('appOptions');
  const isDarkMode = appOptions.uiTheme == UiTheme.Dark;

  useEffect(() => {
    if (appOptions.initialized) {

    }
  }, [appOptions]);

  return (
    <NextUIProvider>
      <ConfigProvider
        theme={{
          algorithm: isDarkMode ? theme.darkAlgorithm : theme.defaultAlgorithm,
        }}
      >
        <BakabaseContext.Provider value={{ isDarkMode }}>
          {children}
        </BakabaseContext.Provider>
      </ConfigProvider>
    </NextUIProvider>
  );
};
