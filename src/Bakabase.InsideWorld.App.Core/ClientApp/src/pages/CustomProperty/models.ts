import { CustomPropertyType } from '@/sdk/constants';

export interface ICustomProperty {
  id: number;
  name: string;
  type: CustomPropertyType;
  categories?: {id: number; name: string}[];
  options?: any;
}
export interface IChoice {
  id: string;
  value?: string;
  color?: string;
  hide?: boolean;
}

export const PropertyTypeIconMap: Record<CustomPropertyType, string> = {
  [CustomPropertyType.SingleLineText]: 'single-line-text',
  [CustomPropertyType.MultilineText]: 'multiline-text',
  [CustomPropertyType.SingleChoice]: 'radiobox',
  [CustomPropertyType.MultipleChoice]: 'multiple-select',
  // [CustomPropertyType.Multilevel]: 'multi_level',
  [CustomPropertyType.Number]: 'number',
  [CustomPropertyType.Percentage]: 'percentage',
  [CustomPropertyType.Rating]: 'star',
  [CustomPropertyType.Boolean]: 'checkboxchecked',
  [CustomPropertyType.Link]: 'link',
  [CustomPropertyType.Attachment]: 'attachment',
  // [CustomPropertyType.Formula]: 'formula'
};

export interface IChoicePropertyOptions {
  choices: IChoice[];
  allowAddingNewOptionsWhileChoosing: boolean;
  defaultValue?: string;
}

export interface INumberPropertyOptions {
  precision: number;
}

export interface IPercentagePropertyOptions {
  precision: number;
  showProgressbar: boolean;
}

export interface IRatingPropertyOptions {
  maxValue: number;
}
