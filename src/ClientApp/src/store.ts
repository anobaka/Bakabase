// src/store.ts
import { createStore } from 'ice';
import clientApp from './models/clientApp';
import Optionses from './models/options';
import backgroundTasks from '@/models/backgroundTasks';
import icons from '@/models/icons';
import downloadTasks from '@/models/downloadTasks';
import iwFsEntryChangeEvents from '@/models/iwFsEntryChangeEvents';
import dependentComponentContexts from '@/models/dependentComponentContexts';
import fileMovingProgresses from '@/models/fileMovingProgresses';
import appUpdaterState from '@/models/appUpdaterState';
import appContext from '@/models/appContext';
import bulkModificationInternals from '@/models/bulkModificationInternals';
import bTasks from '@/models/bTasks';

const {
  appOptions,
  uiOptions,
  bilibiliOptions,
  exHentaiOptions,
  fileSystemOptions,
  javLibraryOptions,
  pixivOptions,
  resourceOptions,
  thirdPartyOptions,
  networkOptions,
  enhancerOptions,
  taskOptions,
  aiOptions,
  soulPlusOptions,
} = Optionses;

export default createStore({
  backgroundTasks,
  icons,
  downloadTasks,
  clientApp,
  iwFsEntryChangeEvents,
  dependentComponentContexts,
  fileMovingProgresses,
  appUpdaterState,
  appContext,
  bulkModificationInternals,
  bTasks,

  appOptions,
  uiOptions,
  bilibiliOptions,
  exHentaiOptions,
  fileSystemOptions,
  javLibraryOptions,
  pixivOptions,
  resourceOptions,
  thirdPartyOptions,
  networkOptions,
  enhancerOptions,
  taskOptions,
  aiOptions,
  soulPlusOptions,
});
