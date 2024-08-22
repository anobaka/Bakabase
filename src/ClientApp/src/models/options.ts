import type {
  BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel,
  BakabaseInfrastructuresComponentsConfigurationsAppAppOptions,
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions,
  BakabaseInsideWorldBusinessConfigurationsModelsInputResourceOptionsPatchInputModel,
  BakabaseInsideWorldModelsConfigsBilibiliOptions,
  BakabaseInsideWorldModelsConfigsExHentaiOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptionsFileMoverOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptionsFileProcessorOptions,
  BakabaseInsideWorldModelsConfigsJavLibraryOptions,
  BakabaseInsideWorldModelsConfigsNetworkOptions,
  BakabaseInsideWorldModelsConfigsPixivOptions,
  BakabaseInsideWorldModelsConfigsThirdPartyOptions,
  BakabaseInsideWorldModelsConfigsUIOptions,
  BakabaseInsideWorldModelsRequestModelsUIOptionsPatchRequestModel,
  BootstrapModelsResponseModelsBaseResponse,
} from '@/sdk/Api';
import BApi from '@/sdk/BApi';
import type { SignalRData } from '@/components/SignalR/models';


interface OptionsStore<TOptions, TPatchModel> {
  state: SignalRData<TOptions>;
  reducers: {
    update: (state: TOptions, payload: any) => any;
  };
  effects: (dispatch) => ({
    save: (patches: TPatchModel) => Promise<any>;
  });
}

const buildModel = <TOptions, TPatchModel>(patchHandler: (patches: TPatchModel) => Promise<BootstrapModelsResponseModelsBaseResponse>) => {
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
      async save(patches: TPatchModel) {
        await patchHandler(patches);
      },
    }),
  } as OptionsStore<TOptions, TPatchModel>;
};

export default {
  appOptions: buildModel<BakabaseInfrastructuresComponentsConfigurationsAppAppOptions, BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel>(BApi.options.patchAppOptions),
  uiOptions: buildModel<BakabaseInsideWorldModelsConfigsUIOptions, BakabaseInsideWorldModelsRequestModelsUIOptionsPatchRequestModel>(BApi.options.patchUiOptions),
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
  resourceOptions: buildModel<BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptions, BakabaseInsideWorldBusinessConfigurationsModelsInputResourceOptionsPatchInputModel>(BApi.options.patchResourceOptions),
  thirdPartyOptions: buildModel<BakabaseInsideWorldModelsConfigsThirdPartyOptions, BakabaseInsideWorldModelsConfigsThirdPartyOptions>(BApi.options.patchThirdPartyOptions),
  networkOptions: buildModel<BakabaseInsideWorldModelsConfigsNetworkOptions, BakabaseInsideWorldModelsConfigsNetworkOptions>(BApi.options.patchNetworkOptions),
};
