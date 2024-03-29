import './global.scss';
import UIHubConnection from '@/components/UIHubConnection';
import dayjs from 'dayjs';
import '@/assets/iconfont/iconfont';

import { defineAppConfig } from 'ice';
import '@/i18n';
import { defineStoreConfig } from '@ice/plugin-store/esm/types';
import BApi from '@/sdk/BApi';
import { UiTheme } from '@/sdk/constants';

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
    strict: true,
  },
}));

export const storeConfig = defineStoreConfig(async () => {
  // todo: find a real portal to finish following actions

  const conn = new UIHubConnection();

  try {
    const { data } = await BApi.options.getAppOptions() || {};
    console.log(data);

    if (data) {
      window.enableAnonymousDataTracking = data.enableAnonymousDataTracking;
      window.appVersion = data.version;
      let uiTheme: UiTheme = data.uiTheme! as number;
      if (uiTheme == UiTheme.FollowSystem) {
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
          uiTheme = UiTheme.Dark;
        } else {
          uiTheme = UiTheme.Light;
        }
      }
      window.uiTheme = uiTheme;

      // if (uiTheme == UiTheme.Dark) {
      //   // import('@alifd/theme-4602/dist/next.css');
      //   import('./tmp.variable.scss');
      //   import('@alifd/theme-design-pro/dist/next.var.css');
      // } else {
      //   import('@alifd/theme-design-pro/dist/next.var.css');
      // }

      if (document) {
        const cls = document.documentElement.classList;
        cls.remove('iw-theme-dark', 'iw-theme-light', 'dark', 'light');
        cls.add(`iw-theme-${uiTheme == UiTheme.Dark ? 'dark' : 'light'}`, uiTheme == UiTheme.Dark ? 'dark' : 'light');
      }

      if (data.enableAnonymousDataTracking) {
        console.log('enable anonymous data tracking');
      }
    }
  } catch (e) {
    console.log(e);
  }

  return {
    initialStates: {},
  };
});
