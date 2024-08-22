import type { Props as PropertyValueRendererProps } from '@/components/Property/components/PropertyValueRenderer';
import type { SearchOperation } from '@/sdk/constants';
import { CustomPropertyType } from '@/sdk/constants';
import { ReservedResourceProperty, SearchableReservedProperty } from '@/sdk/constants';
import { ResourceProperty } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';

export enum FilterValueRendererType {
  Number = 1,
  String,
  DateTime,
  MediaLibrary,
  SingleChoice,
  MultipleChoice,
  Boolean,
  Time,
  Multilevel,
  Tags,
}


export const getFilterValueRendererType = (property: IProperty): FilterValueRendererType | undefined => {
  if (!property.isCustom) {
    const sp = property.id as SearchableReservedProperty;
    switch (sp) {
      case SearchableReservedProperty.Introduction:
        return FilterValueRendererType.String;
      case SearchableReservedProperty.Rating:
        return FilterValueRendererType.Number;
      case SearchableReservedProperty.FileName:
      case SearchableReservedProperty.DirectoryPath: {
        return FilterValueRendererType.String;
      }
      case SearchableReservedProperty.CreatedAt:
      case SearchableReservedProperty.FileCreatedAt:
      case SearchableReservedProperty.FileModifiedAt:
        return FilterValueRendererType.DateTime;
      case SearchableReservedProperty.MediaLibrary:
        return FilterValueRendererType.MediaLibrary;
    }
  } else {
    if (property.type) {
      switch (property.type) {
        case CustomPropertyType.SingleLineText:
        case CustomPropertyType.MultilineText:
          return FilterValueRendererType.String;
        case CustomPropertyType.SingleChoice:
          return FilterValueRendererType.SingleChoice;
        case CustomPropertyType.MultipleChoice:
          return FilterValueRendererType.MultipleChoice;
        case CustomPropertyType.Number:
          return FilterValueRendererType.Number;
        case CustomPropertyType.Percentage:
          return FilterValueRendererType.Number;
        case CustomPropertyType.Rating:
          return FilterValueRendererType.Number;
        case CustomPropertyType.Boolean:
          return FilterValueRendererType.Boolean;
        case CustomPropertyType.Link:
          return FilterValueRendererType.String;
        case CustomPropertyType.Attachment:
          break;
        case CustomPropertyType.Date:
          return FilterValueRendererType.DateTime;
        case CustomPropertyType.DateTime:
          return FilterValueRendererType.DateTime;
        case CustomPropertyType.Time:
          return FilterValueRendererType.Time;
        case CustomPropertyType.Formula:
          break;
        case CustomPropertyType.Multilevel:
          return FilterValueRendererType.Multilevel;
        case CustomPropertyType.Tags:
          return FilterValueRendererType.Tags;
      }
    }
  }

  return;
};
