import type { MarkConfig } from "./types";
import { PathMarkType, PathMatchMode, PropertyValueType } from "@/sdk/constants";

export const parseMarkConfig = (configJson?: string): MarkConfig => {
  try {
    const config = JSON.parse(configJson || "{}");
    return {
      matchMode: config.MatchMode === "Regex" ? PathMatchMode.Regex : PathMatchMode.Layer,
      layer: config.Layer ?? 0,
      regex: config.Regex ?? "",
      propertyPool: config.Pool,
      propertyId: config.PropertyId,
      valueType: config.ValueType === "Dynamic" ? PropertyValueType.Dynamic : PropertyValueType.Fixed,
      fixedValue: config.FixedValue ?? "",
      valueMatchMode: config.ValueRegex ? PathMatchMode.Regex : PathMatchMode.Layer,
      valueLayer: config.ValueLayer ?? 0,
      valueRegex: config.ValueRegex ?? "",
      fsTypeFilter: config.FsTypeFilter,
      extensions: config.Extensions ?? [],
      extensionGroupIds: config.ExtensionGroupIds ?? [],
      mediaLibraryId: config.MediaLibraryId,
      mediaLibraryValueType: config.ValueType === "Dynamic" ? PropertyValueType.Dynamic : PropertyValueType.Fixed,
      layerToMediaLibrary: config.LayerToMediaLibrary ?? 0,
      regexToMediaLibrary: config.RegexToMediaLibrary ?? "",
    };
  } catch {
    return {
      matchMode: PathMatchMode.Layer,
      layer: 0,
      valueType: PropertyValueType.Fixed,
      valueMatchMode: PathMatchMode.Layer,
      valueLayer: 0,
    };
  }
};

export const buildConfigJson = (config: MarkConfig, markType: PathMarkType): string => {
  if (markType === PathMarkType.Resource) {
    return JSON.stringify({
      MatchMode: config.matchMode === PathMatchMode.Layer ? "Layer" : "Regex",
      Layer: config.layer,
      Regex: config.regex,
      FsTypeFilter: config.fsTypeFilter,
      Extensions: config.extensions,
      ExtensionGroupIds: config.extensionGroupIds,
    });
  } else if (markType === PathMarkType.MediaLibrary) {
    return JSON.stringify({
      MatchMode: config.matchMode === PathMatchMode.Layer ? "Layer" : "Regex",
      Layer: config.layer,
      Regex: config.regex,
      ValueType: config.mediaLibraryValueType === PropertyValueType.Fixed ? "Fixed" : "Dynamic",
      MediaLibraryId: config.mediaLibraryValueType === PropertyValueType.Fixed ? config.mediaLibraryId : undefined,
      LayerToMediaLibrary: config.mediaLibraryValueType === PropertyValueType.Dynamic ? config.layerToMediaLibrary : undefined,
      RegexToMediaLibrary: config.mediaLibraryValueType === PropertyValueType.Dynamic ? config.regexToMediaLibrary : undefined,
    });
  } else {
    // Property type
    return JSON.stringify({
      MatchMode: config.matchMode === PathMatchMode.Layer ? "Layer" : "Regex",
      Layer: config.layer,
      Regex: config.regex,
      Pool: config.propertyPool,
      PropertyId: config.propertyId,
      ValueType: config.valueType === PropertyValueType.Fixed ? "Fixed" : "Dynamic",
      FixedValue: config.fixedValue,
      ValueLayer: config.valueLayer,
      ValueRegex: config.valueMatchMode === PathMatchMode.Regex ? config.valueRegex : undefined,
    });
  }
};
