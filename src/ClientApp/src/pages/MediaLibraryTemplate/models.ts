import type { IdName } from '@/components/types';
import type { PropertyPool } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';
import type {
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


export type PathFilter = PathLocator & {
  fsType?: PathFilterFsType;
  extensionGroupIds?: number[];
  extensionGroups?: IdName[];
  extensions?: string[];
};

export type PathLocator = {
  positioner: PathPositioner;
  layer?: number;
  regex?: string;
};

export type MediaLibraryTemplate = {
  id: number;
  name: string;
  resourceFilters?: PathFilter[];
  properties?: MediaLibraryTemplateProperty[];
  playableFileLocator?: MediaLibraryTemplatePlayableFileLocator;
  enhancers?: MediaLibraryTemplateEnhancerOptions[];
  displayNameTemplate?: string;
  samplePaths?: string[];
};


export type MediaLibraryTemplateProperty = {
  pool: PropertyPool;
  id: number;
  property: IProperty;
  valueLocators?: PathLocator[];
};

export type MediaLibraryTemplatePlayableFileLocator = {
  extensionGroups?: IdName[];
  extensionGroupIds?: number[];
  extensions?: string[];
};

export type MediaLibraryTemplateEnhancerOptions = {
  enhancerId: number;
  options?: EnhancerFullOptions;
};
