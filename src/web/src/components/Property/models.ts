import type { PropertyPool, StandardValueType } from "@/sdk/constants";
import type { PropertyType } from "@/sdk/constants";
import type { MultilevelData } from "@/components/StandardValue/models";

export interface IProperty {
  id: number;
  dbValueType: StandardValueType;
  bizValueType: StandardValueType;
  name: string;
  categories?: { id: number; name: string }[];
  options?: any;
  pool: PropertyPool;
  type: PropertyType;
  valueCount?: number;
  typeName: string;
  poolName: string;
  order: number;
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
  // allowAddingNewDataDynamically: boolean;
};

export interface ChoicePropertyOptions {
  choices: IChoice[];
  // allowAddingNewDataDynamically: boolean;
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
  // allowAddingNewDataDynamically: boolean;
  defaultValue?: string;
}
