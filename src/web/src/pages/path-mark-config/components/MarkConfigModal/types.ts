import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";
import { PathMarkType, PathMatchMode, PropertyValueType, PathFilterFsType, PropertyPool, PathMarkApplyScope } from "@/sdk/constants";

export type MarkConfigModalProps = {
  mark?: BakabaseAbstractionsModelsDomainPathMark;
  markType: PathMarkType;
  /** Root path for preview matching (single path) */
  rootPath?: string;
  /** Root paths for preview matching (multiple paths) - takes precedence over rootPath */
  rootPaths?: string[];
  onSave?: (mark: BakabaseAbstractionsModelsDomainPathMark) => Promise<void> | void;
} & DestroyableProps;

export interface MarkConfig {
  matchMode: PathMatchMode;
  layer?: number;
  regex?: string;
  applyScope?: PathMarkApplyScope;
  // For Property type
  propertyPool?: PropertyPool;
  propertyId?: number;
  valueType?: PropertyValueType;
  fixedValue?: string;
  valueMatchMode?: PathMatchMode;
  valueLayer?: number;
  valueRegex?: string;
  // For Resource type
  fsTypeFilter?: PathFilterFsType;
  extensions?: string[];
  extensionGroupIds?: number[];
  isResourceBoundary?: boolean;
  // For MediaLibrary type
  mediaLibraryId?: number;
  mediaLibraryValueType?: PropertyValueType;
  layerToMediaLibrary?: number;
  regexToMediaLibrary?: string;
}
