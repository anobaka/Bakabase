import type { PropertyValueScope, StandardValueType } from '@/sdk/constants';
import type { PropertyPool } from '@/sdk/constants';

type Value = {
  scope: PropertyValueScope;
  value?: any;
  bizValue?: any;
  aliasAppliedBizValue?: any;
};

export type Property = {
  name?: string;
  dbValueType: StandardValueType;
  bizValueType: StandardValueType;
  values?: Value[];
  visible?: boolean;
};

export type Resource = {
  id: number;
  mediaLibraryId: number;
  categoryId: number;
  fileName: string;
  directory: string;
  displayName: string;
  path: string;
  parentId?: number;
  hasChildren: boolean;
  isFile: boolean;
  createDt: string;
  updateDt: string;
  fileCreateDt: string;
  fileUpdateDt: string;
  parent?: Resource;
  properties?: {[key in PropertyPool]?: Record<number, Property>};
  coverPaths?: string[];
  mediaLibraryName?: string;
  category?: {id: number; name: string};
};
