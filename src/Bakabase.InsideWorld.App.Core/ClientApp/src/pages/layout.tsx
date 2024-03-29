import React from 'react';
import { useLocation } from 'ice';
import { ConfigProvider, theme } from 'antd';
import { NextUIProvider } from '@nextui-org/react';
import BasicLayout from '@/layouts/BasicLayout';
import BlankLayout from '@/layouts/BlankLayout';
import { UiTheme } from '@/sdk/constants';
import store from '@/store';

const Layout = () => {
  const location = useLocation();

  if (location.pathname === '/welcome') {
    return (
      <BlankLayout />
    );
  } else {
    return (
      <BasicLayout />
    );
  }
};

export default () => {
  const appOptions = store.useModelState('appOptions');
  const isDarkMode = appOptions.uiTheme == UiTheme.Dark;

  return (
    <NextUIProvider>
      <ConfigProvider
        theme={{
          algorithm: isDarkMode ? theme.darkAlgorithm : theme.defaultAlgorithm,
        }}
      >
        <div className={`${isDarkMode ? 'dark' : 'light'}`}>
          <Layout />
        </div>
      </ConfigProvider>
    </NextUIProvider>
);
};
