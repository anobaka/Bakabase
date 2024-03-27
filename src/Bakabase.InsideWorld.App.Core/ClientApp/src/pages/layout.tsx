import React from 'react';
import { useLocation } from 'ice';
import { ConfigProvider, theme } from 'antd';
import BasicLayout from '@/layouts/BasicLayout';
import BlankLayout from '@/layouts/BlankLayout';
import { UiTheme } from '@/sdk/constants';

export default () => {
  const location = useLocation();

  if (location.pathname === '/welcome') {
    return (
      <BlankLayout />
    );
  } else {
    return (
      <ConfigProvider
        theme={{
          algorithm: window.uiTheme == UiTheme.Dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
        }}
      >
        <BasicLayout />
      </ConfigProvider>
    );
  }
};
