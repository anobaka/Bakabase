import type {
  BakabaseInfrastructuresComponentsConfigurationsAppAppOptions,
  BakabaseInfrastructuresComponentsAppModelsRequestModelsAppOptionsPatchRequestModel,
  BakabaseInsideWorldModelsConfigsUIOptions,
  BakabaseInsideWorldModelsRequestModelsOptionsUIOptionsPatchRequestModel,
  BakabaseInsideWorldModelsConfigsBilibiliOptions,
  BakabaseInsideWorldModelsConfigsExHentaiOptions,
  BakabaseInsideWorldModelsConfigsFileSystemOptions,
  BakabaseInsideWorldModelsConfigsJavLibraryOptions,
  BakabaseInsideWorldModelsConfigsPixivOptions,
  BakabaseInsideWorldModelsConfigsNetworkOptions,
  BakabaseInsideWorldModelsConfigsEnhancerOptions,
  BakabaseInsideWorldModelsConfigsThirdPartyOptions,
  BakabaseInsideWorldModelsRequestModelsOptionsThirdPartyOptionsPatchInput,
  BakabaseInsideWorldModelsConfigsSoulPlusOptions,
  BakabaseInsideWorldModelsRequestModelsOptionsSoulPlusOptionsPatchInputModel,
  BakabaseAbstractionsComponentsConfigurationTaskOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainAiOptions,
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainResourceOptions,
  BakabaseServiceModelsInputResourceOptionsPatchInputModel,
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
  }>((set, get) => ({
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
  BakabaseInsideWorldModelsRequestModelsOptionsUIOptionsPatchRequestModel
>(BApi.options.patchUiOptions);

export const useBilibiliOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsBilibiliOptions,
  BakabaseInsideWorldModelsConfigsBilibiliOptions
>(BApi.options.patchBilibiliOptions);

export const useExHentaiOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsExHentaiOptions,
  BakabaseInsideWorldModelsConfigsExHentaiOptions
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
  BakabaseInsideWorldModelsConfigsPixivOptions,
  BakabaseInsideWorldModelsConfigsPixivOptions
>(BApi.options.patchPixivOptions);

export const useResourceOptionsStore = createOptionStore<
  BakabaseInsideWorldBusinessComponentsConfigurationsModelsDomainResourceOptions,
  BakabaseServiceModelsInputResourceOptionsPatchInputModel
>(BApi.options.patchResourceOptions);

export const useThirdPartyOptionsStore = createOptionStore<
  BakabaseInsideWorldModelsConfigsThirdPartyOptions,
  BakabaseInsideWorldModelsRequestModelsOptionsThirdPartyOptionsPatchInput
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
  BakabaseInsideWorldModelsConfigsSoulPlusOptions,
  BakabaseInsideWorldModelsRequestModelsOptionsSoulPlusOptionsPatchInputModel
>(BApi.options.patchSoulPlusOptions, BApi.options.putSoulPlusOptions);

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
};
