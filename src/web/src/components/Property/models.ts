import type { PropertyPool, StandardValueType } from "@/sdk/constants";
import type { PropertyType } from "@/sdk/constants";
import type { MultilevelData } from "@/components/StandardValue/models";

// Re-export type-safe utilities from PropertySystem
export {
  PropertySystem,
  PropertyAttributeMap,
  getDbValueType,
  getBizValueType,
  isReferenceValueType,
  getAttribute,
  PropertyTypeGroups,
  AllPropertyTypes,
  isValidStandardValue,
  asTypedProperty,
} from "./PropertySystem";

export type {
  PropertyAttribute,
  StandardValueTypeMap,
  StandardValueOf,
  PropertyOptionsMap,
  PropertyOptionsOf,
  PropertyTypeWithOptions,
  PropertyTypeWithoutOptions,
  BaseProperty,
  TypedProperty,
  // Re-export option types from PropertySystem
  ChoiceOption,
  TagOption,
  SingleChoicePropertyOptions,
  MultipleChoicePropertyOptions,
  NumberPropertyOptions as TypedNumberPropertyOptions,
  PercentagePropertyOptions as TypedPercentagePropertyOptions,
  RatingPropertyOptions as TypedRatingPropertyOptions,
  MultilevelPropertyOptions as TypedMultilevelPropertyOptions,
  TagsPropertyOptions as TypedTagsPropertyOptions,
} from "./PropertySystem";

/**
 * Property interface - general purpose, compatible with API responses.
 * For type-safe property handling, use TypedProperty<T> from PropertySystem.
 */
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

/**
 * @deprecated Use ChoiceOption from PropertySystem instead.
 */
export interface IChoice {
  value: string;
  label?: string;
  color?: string;
  hide?: boolean;
}

/**
 * @deprecated Use TagOption from PropertySystem instead.
 */
export type Tag = {
  value: string;
  group?: string;
  name?: string;
  color?: string;
  hide?: boolean;
};

/**
 * @deprecated Use TypedTagsPropertyOptions from PropertySystem instead.
 */
export type TagsPropertyOptions = {
  tags: Tag[];
};

/**
 * @deprecated Use SingleChoicePropertyOptions or MultipleChoicePropertyOptions from PropertySystem instead.
 */
export interface ChoicePropertyOptions {
  choices: IChoice[];
  defaultValue?: string;
}

/**
 * @deprecated Use TypedNumberPropertyOptions from PropertySystem instead.
 */
export interface NumberPropertyOptions {
  precision: number;
}

/**
 * @deprecated Use TypedPercentagePropertyOptions from PropertySystem instead.
 */
export interface PercentagePropertyOptions {
  precision: number;
  showProgressbar: boolean;
}

/**
 * @deprecated Use TypedRatingPropertyOptions from PropertySystem instead.
 */
export interface RatingPropertyOptions {
  maxValue: number;
}

/**
 * @deprecated Use TypedMultilevelPropertyOptions from PropertySystem instead.
 */
export interface MultilevelPropertyOptions {
  data?: MultilevelData<string>[];
  defaultValue?: string;
}
