import type {
  BakabaseAbstractionsComponentsConfigurationTaskOptions,
  BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel,
  BakabaseInfrastructuresComponentsConfigurationsAppAppOptions,
  BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions,
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions,
  BakabaseInsideWorldModelsConfigsBilibiliOptions,
  BakabaseInsideWorldModelsConfigsEnhancerOptions,
  BakabaseInsideWorldModelsConfigsExHentaiOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptionsFileProcessorOptions,
  BakabaseInsideWorldModelsConfigsJavLibraryOptions,
  BakabaseInsideWorldModelsConfigsNetworkOptions,
  BakabaseInsideWorldModelsConfigsPixivOptions, BakabaseInsideWorldModelsConfigsSoulPlusOptions,
  BakabaseInsideWorldModelsConfigsThirdPartyOptions,
  BakabaseInsideWorldModelsConfigsUIOptions, BakabaseInsideWorldModelsRequestModelsSoulPlusOptionsPatchInputModel,
  BakabaseInsideWorldModelsRequestModelsUIOptionsPatchRequestModel,
  BakabaseServiceModelsInputResourceOptionsPatchInputModel,
  BootstrapModelsResponseModelsBaseResponse,
} from '@/sdk/Api';
import {
  BakabaseModulesEnhancerModelsInputCategoryEnhancerOptionsPatchInputModel,
} from '@/sdk/Api';
import BApi from '@/sdk/BApi';
import type { SignalRData } from '@/components/SignalR/models';


export type OptionsStore<TOptions, TPatchModel> = {
  state: SignalRData<TOptions>;
  reducers: {
    update: (state: TOptions, payload: any) => any;
  };
  effects: (dispatch) => ({
    patch: (patches: TPatchModel) => Promise<any>;
    put: (options: TOptions) => Promise<any>;
  });
};

const buildModel = <TOptions, TPatchModel>(
  patchHandler: (patches: TPatchModel) => Promise<BootstrapModelsResponseModelsBaseResponse>,
  putHandler?: (options: TOptions) => Promise<BootstrapModelsResponseModelsBaseResponse>,
) => {
  return {
    state: {} as TOptions,

    // 定义改变该模型状态的纯函数
    reducers: {
      update(state, payload) {
        // console.log('model changing', payload);
        return {
          ...state,
          ...payload,
          initialized: true,
        };
      },
    },

    // 定义处理该模型副作用的函数
    effects: (dispatch) => ({
      async patch(patches: TPatchModel) {
        await patchHandler(patches);
      },
      async put(options: TOptions) {
        await putHandler?.(options);
      },
    }),
  } as OptionsStore<TOptions, TPatchModel>;
};

export default {
  appOptions: buildModel<BakabaseInfrastructuresComponentsConfigurationsAppAppOptions, BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel>(BApi.options.patchAppOptions, BApi.options.putAppOptions),
  uiOptions: buildModel<BakabaseInsideWorldModelsConfigsUIOptions, BakabaseInsideWorldModelsRequestModelsUIOptionsPatchRequestModel>(BApi.options.patchUiOptions, BApi.options.putAiOptions),
  bilibiliOptions: buildModel<BakabaseInsideWorldModelsConfigsBilibiliOptions, BakabaseInsideWorldModelsConfigsBilibiliOptions>(BApi.options.patchBilibiliOptions),
  exHentaiOptions: buildModel<BakabaseInsideWorldModelsConfigsExHentaiOptions, BakabaseInsideWorldModelsConfigsExHentaiOptions>(BApi.options.patchExHentaiOptions),
  fileSystemOptions: buildModel<{
    recentMovingDestinations?: string[];
    fileMover?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions;
    fileProcessor?: BakabaseInsideWorldModelsConfigsFileSystemOptionsFileProcessorOptions;
    decompressionPasswords?: string[];
  }, BakabaseInsideWorldModelsConfigsFileSystemOptions>(BApi.options.patchFileSystemOptions),
  javLibraryOptions: buildModel<BakabaseInsideWorldModelsConfigsJavLibraryOptions, BakabaseInsideWorldModelsConfigsJavLibraryOptions>(BApi.options.patchJavLibraryOptions),
  pixivOptions: buildModel<BakabaseInsideWorldModelsConfigsPixivOptions, BakabaseInsideWorldModelsConfigsPixivOptions>(BApi.options.patchPixivOptions),
  resourceOptions: buildModel<BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions, BakabaseServiceModelsInputResourceOptionsPatchInputModel>(BApi.options.patchResourceOptions),
  thirdPartyOptions: buildModel<BakabaseInsideWorldModelsConfigsThirdPartyOptions, BakabaseInsideWorldModelsConfigsThirdPartyOptions>(BApi.options.patchThirdPartyOptions),
  networkOptions: buildModel<BakabaseInsideWorldModelsConfigsNetworkOptions, BakabaseInsideWorldModelsConfigsNetworkOptions>(BApi.options.patchNetworkOptions),
  enhancerOptions: buildModel<BakabaseInsideWorldModelsConfigsEnhancerOptions, BakabaseInsideWorldModelsConfigsEnhancerOptions>(BApi.options.patchEnhancerOptions),
  taskOptions: buildModel<BakabaseAbstractionsComponentsConfigurationTaskOptions, BakabaseAbstractionsComponentsConfigurationTaskOptions>(BApi.options.patchTaskOptions),
  aiOptions: buildModel<BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions, BakabaseInsideWorldBusinessConfigurationsModelsDomainAiOptions>(BApi.options.patchAiOptions),
  soulPlusOptions: buildModel<BakabaseInsideWorldModelsConfigsSoulPlusOptions, BakabaseInsideWorldModelsRequestModelsSoulPlusOptionsPatchInputModel>(BApi.options.patchSoulPlusOptions),
};
