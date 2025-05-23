import React, { useEffect } from 'react';
import { history, Outlet } from 'ice';
import { Dialog } from '@alifd/next';
import { TourProvider } from '@reactour/tour';
import { useTranslation } from 'react-i18next';
import { Toaster } from 'react-hot-toast';
import styles from './index.module.scss';
import PageNav from './components/PageNav';
import { InitializationContentType, StartupPage } from '@/sdk/constants';
import FloatingAssistant from '@/components/FloatingAssistant';
import { ErrorBoundary } from '@/components/Error';
import BApi from '@/sdk/BApi';
import { buildLogger } from '@/components/utils';
import store from '@/store';

const log = buildLogger('BasicLayout');

export default function BasicLayout() {
  const { t } = useTranslation();

  useEffect(() => {
    log('Initializing...');
    BApi.app.checkAppInitialized().then((a) => {
      switch (a.data) {
        case InitializationContentType.NotAcceptTerms:
          history!.push('/welcome');
          break;
        case InitializationContentType.NeedRestart:
          Dialog.show({
            title: t('Please restart app and try this later'),
            footer: false,
            closeMode: [],
            v2: true,
          });
          break;
      }
    });
  }, []);

  return (
    <TourProvider steps={[]}>
      <ErrorBoundary>
        <div className={styles.insideWorld}>
          <Toaster toastOptions={{
            style: {
              background: '#333',
              // background: 'var(--bakaui-background)',
              color: 'var(--bakaui-color)',
              maxWidth: 800,
            },
          }}
          />
          <FloatingAssistant />
          <PageNav />
          <div className={`${styles.main} pt-1 pb-1 pr-1`}>
            <Outlet />
          </div>
        </div>
      </ErrorBoundary>
    </TourProvider>
  );
}
