import type { StandardValueType } from '@/sdk/constants';
import { ResourceProperty } from '@/sdk/constants';

import { CustomPropertyType } from '@/sdk/constants';
import type { MultilevelData } from '@/components/StandardValue/models';

export interface IProperty {
  id: number;
  dbValueType: StandardValueType;
  bizValueType: StandardValueType;
  name?: string;
  categories?: {id: number; name: string}[];
  options?: any;
  isCustom: boolean;
  type?: CustomPropertyType;
  valueCount?: number;
}

export type PropertyValue = {
  id: number;
  propertyId: number;
  resourceId: number;
  value?: any;
  scope: number;
};

export interface IChoice {
  value: string;
  label?: string;
  color?: string;
  hide?: boolean;
}

export type Tag = {
  value: string;
  group?: string;
  name?: string;
  color?: string;
  hide?: boolean;
};

export type TagsPropertyOptions = {
  tags: Tag[];
  allowAddingNewDataDynamically: boolean;
};

export const PropertyTypeIconMap: {[key in CustomPropertyType]?: string} = {
  [CustomPropertyType.SingleLineText]: 'FontSizeOutlined',
  [CustomPropertyType.MultilineText]: 'OrderedListOutlined',
  [CustomPropertyType.SingleChoice]: 'AppstoreOutlined',
  [CustomPropertyType.MultipleChoice]: 'UnorderedListOutlined',
  [CustomPropertyType.Multilevel]: 'ApartmentOutlined',
  [CustomPropertyType.Number]: 'FieldBinaryOutlined',
  [CustomPropertyType.Percentage]: 'PercentageOutlined',
  [CustomPropertyType.Rating]: 'StarOutlined',
  [CustomPropertyType.Boolean]: 'CheckSquareOutlined',
  [CustomPropertyType.Link]: 'LinkOutlined',
  [CustomPropertyType.Attachment]: 'PaperClipOutlined',
  [CustomPropertyType.Formula]: 'FunctionOutlined',
  [CustomPropertyType.Time]: 'HistoryOutlined',
  [CustomPropertyType.Date]: 'CalendarOutlined',
  [CustomPropertyType.DateTime]: 'CalendarOutlined',
  [CustomPropertyType.Tags]: 'TagsOutlined',
};

export interface ChoicePropertyOptions {
  choices: IChoice[];
  allowAddingNewDataDynamically: boolean;
  defaultValue?: string;
}

export interface NumberPropertyOptions {
  precision: number;
}

export interface PercentagePropertyOptions {
  precision: number;
  showProgressbar: boolean;
}

export interface RatingPropertyOptions {
  maxValue: number;
}

export interface MultilevelPropertyOptions {
  data?: MultilevelData<string>[];
  allowAddingNewDataDynamically: boolean;
  defaultValue?: string;
}

