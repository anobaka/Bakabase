import type { IdName } from '@/components/types';

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
