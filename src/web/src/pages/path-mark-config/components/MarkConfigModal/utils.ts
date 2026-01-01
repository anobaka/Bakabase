import type { MarkConfig } from "./types";
import { PathMarkType, PathMatchMode, PropertyValueType, PathMarkApplyScope } from "@/sdk/constants";

export const parseMarkConfig = (configJson?: string): MarkConfig => {
  try {
    const config = JSON.parse(configJson || "{}");
    return {
      matchMode: config.matchMode ?? PathMatchMode.Layer,
      layer: config.layer ?? 0,
      regex: config.regex ?? "",
      applyScope: config.applyScope ?? PathMarkApplyScope.MatchedOnly,
      propertyPool: config.pool,
      propertyId: config.propertyId,
      valueType: config.valueType ?? PropertyValueType.Fixed,
      fixedValue: config.fixedValue ?? "",
      valueMatchMode: config.valueRegex ? PathMatchMode.Regex : PathMatchMode.Layer,
      valueLayer: config.valueLayer ?? 0,
      valueRegex: config.valueRegex ?? "",
      fsTypeFilter: config.fsTypeFilter,
      extensions: config.extensions ?? [],
      extensionGroupIds: config.extensionGroupIds ?? [],
      isResourceBoundary: config.isResourceBoundary ?? false,
      mediaLibraryId: config.mediaLibraryId,
      mediaLibraryValueType: config.valueType ?? PropertyValueType.Fixed,
      layerToMediaLibrary: config.layerToMediaLibrary ?? 0,
      regexToMediaLibrary: config.regexToMediaLibrary ?? "",
    };
  } catch {
    return {
      matchMode: PathMatchMode.Layer,
      layer: 0,
      applyScope: PathMarkApplyScope.MatchedOnly,
      valueType: PropertyValueType.Fixed,
      valueMatchMode: PathMatchMode.Layer,
      valueLayer: 0,
    };
  }
};

export const buildConfigJson = (config: MarkConfig, markType: PathMarkType): string => {
  if (markType === PathMarkType.Resource) {
    return JSON.stringify({
      matchMode: config.matchMode,
      layer: config.layer,
      regex: config.regex,
      fsTypeFilter: config.fsTypeFilter,
      extensions: config.extensions,
      extensionGroupIds: config.extensionGroupIds,
      applyScope: config.applyScope ?? PathMarkApplyScope.MatchedOnly,
      isResourceBoundary: config.isResourceBoundary ?? false,
    });
  } else if (markType === PathMarkType.MediaLibrary) {
    return JSON.stringify({
      matchMode: config.matchMode,
      layer: config.layer,
      regex: config.regex,
      valueType: config.mediaLibraryValueType,
      mediaLibraryId: config.mediaLibraryValueType === PropertyValueType.Fixed ? config.mediaLibraryId : undefined,
      layerToMediaLibrary: config.mediaLibraryValueType === PropertyValueType.Dynamic ? config.layerToMediaLibrary : undefined,
      regexToMediaLibrary: config.mediaLibraryValueType === PropertyValueType.Dynamic ? config.regexToMediaLibrary : undefined,
      applyScope: config.applyScope ?? PathMarkApplyScope.MatchedOnly,
    });
  } else {
    // Property type
    return JSON.stringify({
      matchMode: config.matchMode,
      layer: config.layer,
      regex: config.regex,
      pool: config.propertyPool,
      propertyId: config.propertyId,
      valueType: config.valueType,
      fixedValue: config.fixedValue,
      valueLayer: config.valueLayer,
      valueRegex: config.valueMatchMode === PathMatchMode.Regex ? config.valueRegex : undefined,
      applyScope: config.applyScope ?? PathMarkApplyScope.MatchedOnly,
    });
  }
};
