import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";
import { PathMarkType, PathMatchMode, PropertyValueType, PathFilterFsType, PropertyPool } from "@/sdk/constants";

export type MarkConfigModalProps = {
  mark?: BakabaseAbstractionsModelsDomainPathMark;
  markType: PathMarkType;
  /** Root path for preview matching */
  rootPath?: string;
  onSave?: (mark: BakabaseAbstractionsModelsDomainPathMark) => Promise<void> | void;
} & DestroyableProps;

export interface MarkConfig {
  matchMode: PathMatchMode;
  layer?: number;
  regex?: string;
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
}
