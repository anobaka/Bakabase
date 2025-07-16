import './global.scss';
import UIHubConnection from '@/components/SignalR/UIHubConnection';
import dayjs from 'dayjs';
import '@/assets/iconfont/iconfont';

import { defineAppConfig } from 'ice';
import '@/i18n';
import { defineStoreConfig } from '@ice/plugin-store/esm/types';
import BApi from '@/sdk/BApi';
import { UiTheme } from '@/sdk/constants';
import { getUiTheme } from '@/components/utils';

const duration = require('dayjs/plugin/duration');
dayjs.extend(duration);

// export const dataLoader = defineDataLoader(async () => {
//   const { data } = await BApi.options.getAppOptions() || {};
//   return data;
// });

export default defineAppConfig(() => ({
  router: {
    type: 'hash',
  },
  app: {
    // errorBoundary: true,
    // will cause rendering twice in development mode
    // strict: true,ga
  },
}));

export const storeConfig = defineStoreConfig(async () => {
  // todo: find a real portal to finish following actions

  const conn = new UIHubConnection();

  try {
    const { data } = await BApi.options.getAppOptions() || {};
    console.log('AppOptions', data);

    if (data) {
      window.enableAnonymousDataTracking = data.enableAnonymousDataTracking;
      window.appVersion = data.version;
      const uiTheme = getUiTheme(data);
      window.uiTheme = String(uiTheme);

      console.log('xxxxxxx', uiTheme);

      if (document) {
        const cls = document.documentElement.classList;
        cls.remove('iw-theme-dark', 'iw-theme-light', 'dark', 'light');
        cls.add(`iw-theme-${uiTheme == UiTheme.Dark ? 'dark' : 'light'}`, uiTheme == UiTheme.Dark ? 'dark' : 'light');
      }

      if (data.enableAnonymousDataTracking) {
        console.log('enable anonymous data tracking');
        if (typeof window.clarity === 'function' && window.appVersion) {
          window.clarity('set', 'appVersion', window.appVersion);
        }
      }
    }
  } catch (e) {
    console.log(e);
  }

  return {
    initialStates: {},
  };
});

declare global {
  interface Window {
    clarity?: (action: string, key: string, value: any) => void;
    appVersion?: string;
    enableAnonymousDataTracking?: boolean;
    uiTheme?: string;
  }
}
