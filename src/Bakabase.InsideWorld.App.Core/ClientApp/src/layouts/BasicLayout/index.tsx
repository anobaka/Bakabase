import React, { useEffect, useState } from 'react';
import './index.scss';
import { Link, history, useLocation, Outlet } from 'ice';
import { Button, Dialog } from '@alifd/next';
import i18n from 'i18next';
import { useNavigate } from '@ice/runtime/router';
import PageNav from './components/PageNav';
import { CheckAppInitialized } from '@/sdk/apis';
import { InitializationContentType } from '@/sdk/constants';
import FloatingAssistant from '@/components/FloatingAssistant';
import ErrorBoundary from '@/components/ErrorBoundary';

export default function BasicLayout({
                                      children,
                                    }: {
  children: React.ReactNode;
}) {
  const location = useLocation();

  useEffect(() => {
    CheckAppInitialized()
      .invoke((a) => {
        switch (a.data) {
          case InitializationContentType.NotAcceptTerms:
            history.push('/welcome');
            break;
          case InitializationContentType.NeedRestart:
            Dialog.show({
              title: i18n.t('Please restart app and try this later'),
              footer: false,
              closeMode: [],
              closeable: false,
            });
            break;
        }
      })
      .catch((a) => {
        history.push('/welcome');
      });
  }, []);


  return (
    <ErrorBoundary>
      <div className={'inside-world'}>
        <FloatingAssistant />
        <PageNav />
        <div className={'main'}>
          <Outlet />
        </div>
      </div>
    </ErrorBoundary>
  );
}
