import type { StandardValueType } from '@/sdk/constants';

import { CustomPropertyType } from '@/sdk/constants';
import type { MultilevelData } from '@/components/StandardValue/models';

export interface IProperty {
  id: number;
  valueType: StandardValueType;
  name?: string;
  categories?: {id: number; name: string}[];
  options?: any;
  isReserved: boolean;
  type?: CustomPropertyType;
}
export interface IChoice {
  id: string;
  value?: string;
  color?: string;
  hide?: boolean;
}

export const PropertyTypeIconMap: {[key in CustomPropertyType]?: string} = {
  [CustomPropertyType.SingleLineText]: 'single-line-text',
  [CustomPropertyType.MultilineText]: 'multiline-text',
  [CustomPropertyType.SingleChoice]: 'radiobox',
  [CustomPropertyType.MultipleChoice]: 'multiple-select',
  [CustomPropertyType.Multilevel]: 'multi_level',
  [CustomPropertyType.Number]: 'number',
  [CustomPropertyType.Percentage]: 'percentage',
  [CustomPropertyType.Rating]: 'star',
  [CustomPropertyType.Boolean]: 'checkboxchecked',
  [CustomPropertyType.Link]: 'link',
  [CustomPropertyType.Attachment]: 'attachment',
  [CustomPropertyType.Formula]: 'formula',
  [CustomPropertyType.Time]: 'time',
  [CustomPropertyType.Date]: 'calendar-alt',
  [CustomPropertyType.DateTime]: 'date-time',
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
