import { PropertyType, SearchOperation } from "@/sdk/constants";

/**
 * Maps property type to default operation(s) for simple filter mode.
 * For range types (Number, Date, etc.), returns a tuple of [min, max] operations.
 */
export const SimpleFilterOperationMap: Record<PropertyType, SearchOperation | [SearchOperation, SearchOperation]> = {
  [PropertyType.SingleLineText]: SearchOperation.Contains,
  [PropertyType.MultilineText]: SearchOperation.Contains,
  [PropertyType.SingleChoice]: SearchOperation.In,
  [PropertyType.MultipleChoice]: SearchOperation.Contains,
  [PropertyType.Number]: [SearchOperation.GreaterThanOrEquals, SearchOperation.LessThanOrEquals],
  [PropertyType.Percentage]: [SearchOperation.GreaterThanOrEquals, SearchOperation.LessThanOrEquals],
  [PropertyType.Rating]: [SearchOperation.GreaterThanOrEquals, SearchOperation.LessThanOrEquals],
  [PropertyType.Boolean]: SearchOperation.Equals,
  [PropertyType.Link]: SearchOperation.Contains,
  [PropertyType.Attachment]: SearchOperation.Contains,
  [PropertyType.Date]: [SearchOperation.GreaterThanOrEquals, SearchOperation.LessThanOrEquals],
  [PropertyType.DateTime]: [SearchOperation.GreaterThanOrEquals, SearchOperation.LessThanOrEquals],
  [PropertyType.Time]: [SearchOperation.GreaterThanOrEquals, SearchOperation.LessThanOrEquals],
  [PropertyType.Formula]: SearchOperation.Contains,
  [PropertyType.Multilevel]: SearchOperation.Contains,
  [PropertyType.Tags]: SearchOperation.Contains,
};

/**
 * Check if property type uses range filter (two filters: min and max).
 */
export const isRangeFilterType = (type: PropertyType): boolean => {
  const operation = SimpleFilterOperationMap[type];
  return Array.isArray(operation);
};

/**
 * Get the default operation for a property type in simple mode.
 * For range types, returns the first operation (GreaterThanOrEquals).
 */
export const getSimpleFilterOperation = (type: PropertyType): SearchOperation => {
  const operation = SimpleFilterOperationMap[type];
  return Array.isArray(operation) ? operation[0] : operation;
};

/**
 * Get both operations for a range filter type.
 * Returns undefined for non-range types.
 */
export const getRangeFilterOperations = (type: PropertyType): [SearchOperation, SearchOperation] | undefined => {
  const operation = SimpleFilterOperationMap[type];
  return Array.isArray(operation) ? operation : undefined;
};
