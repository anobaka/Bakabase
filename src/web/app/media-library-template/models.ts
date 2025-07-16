import type { IdName } from '@/components/types';
import type { PathPropertyExtractorBasePathType, PropertyPool } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';
import type { EnhancerTargetFullOptions } from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';
import {
  EnhancerFullOptions,
} from '@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models';

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

export type MediaLibraryTemplate = {
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
  child?: MediaLibraryTemplate;
};


export type MediaLibraryTemplateProperty = {
  pool: PropertyPool;
  id: number;
  property: Omit<IProperty, 'bizValueType' | 'dbValueType'>;
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
};
