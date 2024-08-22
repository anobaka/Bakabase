import type { BulkModificationProperty,
  BulkModificationFilterOperation } from '@/sdk/constants';
import {
  BulkModificationFilterOperation as FO,
  bulkModificationProperties,
  BulkModificationProperty as P,
} from '@/sdk/constants';

export enum FilterTargetComponentValueType {
  None,
  Text,
  Number,
  Datetime,
  Category,
  MediaLibrary,
  Tag,
  Language,
}

const VT = FilterTargetComponentValueType;

export class FilterTargetComponent {
  valueType: FilterTargetComponentValueType;
  multiple: boolean = false;
}

const PropertyFilterValueTypeMap = {
  [VT.Category]: [P.Category],
  [VT.MediaLibrary]: [P.MediaLibrary],
  [VT.Text]: [P.Name, P.Publisher, P.Volume, P.Original, P.Series, P.Introduction, P.CustomProperty, P.DirectoryPath, P.FileName],
  [VT.Datetime]: [P.ReleaseDt, P.CreateDt, P.FileCreateDt, P.FileModifyDt],
  [VT.Number]: [P.Rate],
  [VT.Tag]: [P.Tag],
  [VT.Language]: [P.Language],
};

const FilterValueTypePropertyMap = Object.keys(PropertyFilterValueTypeMap).reduce<{ [type in BulkModificationProperty]?: FilterTargetComponentValueType }>((s, t) => {
  for (const p of PropertyFilterValueTypeMap[t]) {
    s[p] = parseInt(t, 10);
  }
  return s;
}, {});

const FilterValueTypeOperationMap = {
  [VT.Category]: [FO.In, FO.NotIn, FO.Equals, FO.NotEquals],
  [VT.MediaLibrary]: [FO.In, FO.NotIn, FO.Equals, FO.NotEquals],
  [VT.Text]: [FO.In, FO.NotIn, FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith, FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.Matches, FO.NotMatches],
  [VT.Datetime]: [FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn],
  [VT.Number]: [FO.In, FO.NotIn, FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals, FO.IsNotNull, FO.IsNull],
  [VT.Tag]: [FO.In, FO.NotIn, FO.Contains, FO.NotContains, FO.IsNotNull, FO.IsNull],
  [VT.Language]: [FO.In, FO.NotIn, FO.Equals, FO.NotEquals, FO.IsNotNull, FO.IsNull],
};

const FilterOperationMultipleMap = {
  [FO.In]: true,
  [FO.NotIn]: true,
  [FO.Equals]: false,
  [FO.NotEquals]: false,
  [FO.Contains]: false,
  [FO.NotContains]: false,
  [FO.StartsWith]: false,
  [FO.NotStartsWith]: false,
  [FO.EndsWith]: false,
  [FO.NotEndsWith]: false,
  [FO.GreaterThan]: false,
  [FO.LessThan]: false,
  [FO.GreaterThanOrEquals]: false,
  [FO.LessThanOrEquals]: false,
  [FO.IsNull]: false,
  [FO.IsNotNull]: false,
  [FO.Matches]: false,
  [FO.NotMatches]: false,
};

export const FindInAliasesProperties = [P.Name, P.Publisher, P.Volume, P.Original, P.Series, P.Introduction, P.CustomProperty];

const filterTargetComponents = bulkModificationProperties.reduce<{ [type in BulkModificationProperty]?: { [type in BulkModificationFilterOperation]?: FilterTargetComponent } }>((s, property) => {
  const tmp: { [type in BulkModificationFilterOperation]?: FilterTargetComponent } = s[property.value] = {};
  const valueType = FilterValueTypePropertyMap[property.value];
  if (valueType) {
    const operations = FilterValueTypeOperationMap[valueType];
    if (operations) {
      for (const o of operations) {
        const c = new FilterTargetComponent();
        c.valueType = o == FO.IsNull || o == FO.IsNotNull ? FilterTargetComponentValueType.None : FilterValueTypePropertyMap[property.value];
        c.multiple = FilterOperationMultipleMap[o];
        tmp[o] = c;
      }
    }
  }
  return s;
}, {});

// console.log(4444, filterTargetComponents);

export {
  filterTargetComponents,
};
