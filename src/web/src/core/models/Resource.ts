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

export type ResourceSourceLink = {
  id: number;
  resourceId: number;
  source: ResourceSource;
  sourceKey: string;
  coverUrls?: string[];
  localCoverPaths?: string[];
};

export type Resource = {
  id: number;
  mediaLibraryId: number;
  categoryId: number;
  status: ResourceStatus;
  sourceLinks?: ResourceSourceLink[];
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

  /** Final resolved cover paths, populated by backend using priority-based selection */
  covers?: string[];
  /** Final resolved playable items from all sources */
  playableItems?: PlayableItem[];
  /** Whether there are more playable files beyond what's cached */
  hasMorePlayableFiles: boolean;
  /** Whether covers have been resolved and are ready */
  coversReady: boolean;
  /** Whether playable items have been resolved and are ready */
  playableItemsReady: boolean;

  /** Filesystem-level cache (covers and playable files discovered from filesystem) */
  cache?: {
    playableFilePaths?: string[];
    hasMorePlayableFiles: boolean;
    coverPaths?: string[];
    cachedTypes: ResourceCacheType[];
  };
};
