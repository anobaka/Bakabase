import type {
  PropertyType,
  PropertyValueScope,
  ResourceCacheType,
  ResourceSource,
  ResourceStatus,
  ResourceTag,
  StandardValueType,
} from "@/sdk/constants";
import type { PropertyPool } from "@/sdk/constants";

type Value = {
  scope: PropertyValueScope;
  value?: any;
  bizValue?: any;
  aliasAppliedBizValue?: any;
};

export type Property = {
  name?: string;
  type: PropertyType;
  dbValueType: StandardValueType;
  bizValueType: StandardValueType;
  values?: Value[];
  visible?: boolean;
  order: number;
};

export type PlayableItem = {
  source: ResourceSource;
  key: string;
  displayName?: string;
};

export type Resource = {
  id: number;
  mediaLibraryId: number;
  categoryId: number;
  source: ResourceSource;
  status: ResourceStatus;
  sourceKey: string;
  fileName?: string;
  directory?: string;
  displayName?: string;
  path?: string;
  parentId?: number;
  hasChildren: boolean;
  isFile: boolean;
  createdAt: string;
  updatedAt: string;
  fileCreatedAt: string;
  fileModifiedAt: string;
  parent?: Resource;
  properties?: { [key in PropertyPool]?: Record<number, Property> };
  coverPaths?: string[];
  /** @deprecated */
  mediaLibraryName?: string;
  /** @deprecated */
  mediaLibraryColor?: string;
  mediaLibraries?: { id: number; name: string; color?: string }[];
  /** @deprecated */
  category?: { id: number; name: string };
  pinned: boolean;
  tags: ResourceTag[];
  playedAt?: string;
  cache?: {
    playableFilePaths?: string[];
    playableItems?: PlayableItem[];
    hasMorePlayableFiles: boolean;
    coverPaths?: string[];
    cachedTypes: ResourceCacheType[];
  };
};
