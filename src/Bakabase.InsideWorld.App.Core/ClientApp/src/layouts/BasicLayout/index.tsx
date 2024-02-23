import React, { useEffect } from 'react';
import './index.scss';
import { history, Outlet, useLocation } from 'ice';
import { Dialog } from '@alifd/next';
import i18n from 'i18next';
import { TourProvider } from '@reactour/tour';
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

  console.log('444444444444444444444444444');


  return (
    <TourProvider steps={[]}>
      <ErrorBoundary>
        <div className={'inside-world'}>
          <FloatingAssistant />
          <PageNav />
          <div className={'main'}>
            <Outlet />
          </div>
        </div>
      </ErrorBoundary>
    </TourProvider>
  );
}
