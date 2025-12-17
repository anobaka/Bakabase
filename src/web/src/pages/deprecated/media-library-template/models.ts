import type { IdName } from "@/components/types.ts";
import type { IProperty } from "@/components/Property/models.ts";
import type { EnhancerTargetFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models.ts";
import type {
  EnhancerId,
  PathPropertyExtractorBasePathType,
  PropertyPool,
  PropertyValueScope,
} from "@/sdk/constants.ts";

export enum PathPositioner {
  Layer = 1,
  Regex = 2,
}

export enum PathFilterFsType {
  File = 1,
  Directory = 2,
}

export type PathFilter = {
  positioner: PathPositioner;
  layer?: number;
  regex?: string;
  fsType?: PathFilterFsType;
  extensionGroupIds?: number[];
  extensionGroups?: IdName[];
  extensions?: string[];
};

export type PathPropertyExtractor = {
  basePathType: PathPropertyExtractorBasePathType;
  positioner: PathPositioner;
  layer?: number;
  regex?: string;
};

export type MediaLibraryTemplatePage = {
  id: number;
  name: string;
  author?: string;
  description?: string;
  resourceFilters?: PathFilter[];
  properties?: MediaLibraryTemplateProperty[];
  playableFileLocator?: MediaLibraryTemplatePlayableFileLocator;
  enhancers?: MediaLibraryTemplateEnhancerOptions[];
  displayNameTemplate?: string;
  samplePaths?: string[];
  createdAt: string;
  childTemplateId?: number;
  child?: MediaLibraryTemplatePage;
};

export type MediaLibraryTemplateProperty = {
  pool: PropertyPool;
  id: number;
  property: Omit<IProperty, "bizValueType" | "dbValueType">;
  valueLocators?: PathPropertyExtractor[];
};

export type MediaLibraryTemplatePlayableFileLocator = {
  extensionGroups?: IdName[];
  extensionGroupIds?: number[];
  extensions?: string[];
  maxFileCount?: number;
};

export type MediaLibraryTemplateEnhancerOptions = {
  enhancerId: number;
  targetOptions?: EnhancerTargetFullOptions[];
  expressions?: string[];
  requirements?: EnhancerId[];
  keywordProperty?: {
    pool: PropertyPool;
    id: number;
    scope: PropertyValueScope;
  };
  pretreatKeyword?: boolean;
};
