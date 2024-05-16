import type { IProperty, PropertyValue } from '@/components/Property/models';

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
  customPropertiesV2?: IProperty[];
  customPropertyValues?: PropertyValue[];
};
