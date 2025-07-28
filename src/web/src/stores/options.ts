import type {
  BakabaseInfrastructuresComponentsConfigurationsAppAppOptions,
  BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel,
  BakabaseInsideWorldModelsConfigsUIOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputUIOptionsPatchRequestModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainBilibiliOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainExHentaiOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptions,
  BakabaseInsideWorldModelsConfigsJavLibraryOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainPixivOptions,
  BakabaseInsideWorldModelsConfigsNetworkOptions,
  BakabaseInsideWorldModelsConfigsEnhancerOptions,
  BakabaseInsideWorldModelsConfigsThirdPartyOptions,
  BakabaseAbstractionsComponentsConfigurationTaskOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainResourceOptions,
  BakabaseServiceModelsInputResourceOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputBilibiliOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputExHentaiOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputPixivOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputThirdPartyOptionsPatchInput,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainSoulPlusOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputSoulPlusOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainBangumiOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputBangumiOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainCienOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputCienOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainDLsiteOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputDLsiteOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainFanboxOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputFanboxOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainFantiaOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputFantiaOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainPatreonOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputPatreonOptionsPatchInputModel,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainTmdbOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputTmdbOptionsPatchInputModel,
} from "@/sdk/Api";

import { create } from "zustand";

import BApi from "@/sdk/BApi";

// 通用工厂
function createOptionStore<T, TPatch>(
  updateApi: (patch: TPatch) => Promise<any>,
  putApi?: (data: T) => Promise<any>,
) {
  return create<{
    data: T;
    initialized?: boolean;
    update: (payload: Partial<T>) => void;
    patch: (patches: TPatch) => Promise<void>;
    put?: (data: T) => Promise<void>;
  }>((set) => ({
    data: {} as T,
    initialized: false,
    update: (payload) =>
      set((state) => ({
        data: { ...state.data, ...payload },
        initialized: true,
      })),
    patch: async (patches) => {
      await updateApi(patches);
    },
    put: putApi
      ? async (data) => {
          await putApi(data);
        }
      : undefined,
  }));
}

export const useAppOptionsStore = createOptionStore<
  BakabaseInfrastructuresComponentsConfigurationsAppAppOptions,
  BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel
>(BApi.options.patchAppOptions, BApi.options.putAppOptions);

export const useUiOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsUIOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputUIOptionsPatchRequestModel
>(BApi.options.patchUiOptions);

export const useBilibiliOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainBilibiliOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputBilibiliOptionsPatchInputModel
>(BApi.options.patchBilibiliOptions);

export const useExHentaiOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainExHentaiOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputExHentaiOptionsPatchInputModel
>(BApi.options.patchExHentaiOptions);

export const useFileSystemOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsFileSystemOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptions
>(BApi.options.patchFileSystemOptions);

export const useJavLibraryOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsJavLibraryOptions,
  BakabaseInsideWorldModelsConfigsJavLibraryOptions
>(BApi.options.patchJavLibraryOptions);

export const usePixivOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainPixivOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputPixivOptionsPatchInputModel
>(BApi.options.patchPixivOptions);

export const useResourceOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainResourceOptions,
  BakabaseServiceModelsInputResourceOptionsPatchInputModel
>(BApi.options.patchResourceOptions);

export const useThirdPartyOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsThirdPartyOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputThirdPartyOptionsPatchInput
>(BApi.options.patchThirdPartyOptions, BApi.options.putThirdPartyOptions);

export const useNetworkOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsNetworkOptions,
  BakabaseInsideWorldModelsConfigsNetworkOptions
>(BApi.options.patchNetworkOptions);

export const useEnhancerOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsEnhancerOptions,
  BakabaseInsideWorldModelsConfigsEnhancerOptions
>(BApi.options.patchEnhancerOptions);

export const useTaskOptionsStore = createOptionStore<
  BakabaseAbstractionsComponentsConfigurationTaskOptions,
  BakabaseAbstractionsComponentsConfigurationTaskOptions
>(BApi.options.patchTaskOptions);

export const useAiOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions
>(BApi.options.patchAiOptions, BApi.options.putAiOptions);

export const useSoulPlusOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainSoulPlusOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputSoulPlusOptionsPatchInputModel
>(BApi.options.patchSoulPlusOptions, BApi.options.putSoulPlusOptions);

export const useBangumiOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainBangumiOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputBangumiOptionsPatchInputModel
>(BApi.options.patchBangumiOptions);

export const useCienOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainCienOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputCienOptionsPatchInputModel
>(BApi.options.patchCienOptions);

export const useDLsiteOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainDLsiteOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputDLsiteOptionsPatchInputModel
>(BApi.options.patchDLsiteOptions);

export const useFanboxOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainFanboxOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputFanboxOptionsPatchInputModel
>(BApi.options.patchFanboxOptions);

export const useFantiaOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainFantiaOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputFantiaOptionsPatchInputModel
>(BApi.options.patchFantiaOptions);

export const usePatreonOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainPatreonOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputPatreonOptionsPatchInputModel
>(BApi.options.patchPatreonOptions);

export const useTmdbOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainTmdbOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputTmdbOptionsPatchInputModel
>(BApi.options.patchTmdbOptions);

export const optionsStores = {
  appOptions: useAppOptionsStore,
  uiOptions: useUiOptionsStore,
  bilibiliOptions: useBilibiliOptionsStore,
  exHentaiOptions: useExHentaiOptionsStore,
  fileSystemOptions: useFileSystemOptionsStore,
  javLibraryOptions: useJavLibraryOptionsStore,
  pixivOptions: usePixivOptionsStore,
  resourceOptions: useResourceOptionsStore,
  thirdPartyOptions: useThirdPartyOptionsStore,
  networkOptions: useNetworkOptionsStore,
  enhancerOptions: useEnhancerOptionsStore,
  taskOptions: useTaskOptionsStore,
  aiOptions: useAiOptionsStore,
  soulPlusOptions: useSoulPlusOptionsStore,
  bangumiOptions: useBangumiOptionsStore,
  cienOptions: useCienOptionsStore,
  dlsiteOptions: useDLsiteOptionsStore,
  fanboxOptions: useFanboxOptionsStore,
  fantiaOptions: useFantiaOptionsStore,
  patreonOptions: usePatreonOptionsStore,
  tmdbOptions: useTmdbOptionsStore,
};
