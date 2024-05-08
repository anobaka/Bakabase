import type { IProperty } from '@/components/Property/models';
import { CustomPropertyType, ResourceProperty } from '@/sdk/constants';

interface Props {
  value?: any;
  onChange?: (v: any) => any;
  property: IProperty;
  editing: boolean;
}

export default ({ value, onChange, property, editing }: Props) => {
  if (property.isReserved) {
    const type = property.id as ResourceProperty;
    switch (type) {
      case ResourceProperty.FileName:
        break;
      case ResourceProperty.DirectoryPath:
        break;
      case ResourceProperty.CreatedAt:
        break;
      case ResourceProperty.FileCreatedAt:
        break;
      case ResourceProperty.FileModifiedAt:
        break;
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
};
