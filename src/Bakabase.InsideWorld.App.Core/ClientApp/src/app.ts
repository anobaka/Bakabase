import './global.scss';
import UIHubConnection from '@/components/UIHubConnection';
import dayjs from 'dayjs';
import '@/assets/iconfont/iconfont';

import { defineAppConfig } from 'ice';
import { defineDataLoader } from '@ice/runtime';
import '@/i18n';
import { defineStoreConfig } from '@ice/plugin-store/esm/types';
import BApi from '@/sdk/BApi';

const duration = require('dayjs/plugin/duration');
dayjs.extend(duration);

export const dataLoader = defineDataLoader(async () => {

});

export default defineAppConfig(() => ({
  router: {
    type: 'hash',
  },
  app: {
    // errorBoundary: true,
  },
}));

export const storeConfig = defineStoreConfig(async () => {
  const conn = new UIHubConnection();

  try {
    const { data } = await BApi.options.getAppOptions() || {};
    console.log(data);

    window.enableAnonymousDataTracking = data.enableAnonymousDataTracking;
    window.appVersion = data.version;

    if (data.enableAnonymousDataTracking) {
      console.log('enable anonymous data tracking');
    }
  } catch (e) {
    console.log(e);
  }

  return {
    initialStates: {

    },
  };
});
