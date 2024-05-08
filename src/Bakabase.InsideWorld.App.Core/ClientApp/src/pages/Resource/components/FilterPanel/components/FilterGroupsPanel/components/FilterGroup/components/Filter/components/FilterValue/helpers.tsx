import type { FilterValueContext } from './models';
import ValueRenderer
  from './components/ValueRenderer';
import type { IProperty } from '@/components/Property/models';
import { CustomPropertyType, ResourceProperty } from '@/sdk/constants';
import StringValueEditor
  from '@/pages/Resource/components/FilterPanel/components/FilterGroupsPanel/components/FilterGroup/components/Filter/components/FilterValue/components/ValueEditor/StringValueEditor';

export function buildFilterValueContext(property: IProperty, value?: any): FilterValueContext {
  const jo = value === null || value === undefined ? null : JSON.parse(value);

  if (property.isReserved) {
    const type = property.id as ResourceProperty;
    switch (type) {
      case ResourceProperty.FileName:
      case ResourceProperty.DirectoryPath:
      case ResourceProperty.CreatedAt:
      case ResourceProperty.FileCreatedAt:
      case ResourceProperty.FileModifiedAt:
      {
        const typedValue = jo as string;
        return {
          displayValue: typedValue,
          ValueComponent: ValueRenderer,
          EditorComponent: StringValueEditor,
        };
      }
      case ResourceProperty.Category:
        break;
      case ResourceProperty.MediaLibrary:
        break;
    }
  } else {
    switch (property.type as CustomPropertyType) {
      case CustomPropertyType.SingleLineText:
        break;
      case CustomPropertyType.MultilineText:
        break;
      case CustomPropertyType.SingleChoice:
        break;
      case CustomPropertyType.MultipleChoice:
        break;
      case CustomPropertyType.Number:
        break;
      case CustomPropertyType.Percentage:
        break;
      case CustomPropertyType.Rating:
        break;
      case CustomPropertyType.Boolean:
        break;
      case CustomPropertyType.Link:
        break;
      case CustomPropertyType.Attachment:
        break;
      case CustomPropertyType.Date:
        break;
      case CustomPropertyType.DateTime:
        break;
      case CustomPropertyType.Time:
        break;
      case CustomPropertyType.Formula:
        break;
      case CustomPropertyType.Multilevel:
        break;
    }
  }

  return;
}
