import { useTranslation } from 'react-i18next';
import { CustomPropertyType } from '@/sdk/constants';
import type { StandardValueType } from '@/sdk/constants';
import {
  StringValueRenderer,
  ChoiceValueRenderer,
  MultilineStringValueRenderer,
  NumberValueRenderer,
  RatingValueRenderer,
  BooleanValueRenderer,
  AttachmentValueRenderer,
  DateTimeValueRenderer,
  TimeValueRenderer,
  FormulaValueRenderer, MultilevelValueRenderer, TagsValueRenderer,
} from '@/components/StandardValue';
import LinkValueRenderer from '@/components/StandardValue/ValueRenderer/Renderers/LinkValueRenderer';

type Props = {
  id: number;
  type: CustomPropertyType;
  bizValue?: any;
  bizValueType?: StandardValueType;
  editable?: boolean;
  onValueChange?: (value?: any) => any;
  variant?: 'default' | 'light';
};

export default ({ type, bizValue, bizValueType, editable, onValueChange, variant }: Props) => {
  const { t } = useTranslation();

  switch (type) {
    case CustomPropertyType.SingleLineText:
      return (
        <StringValueRenderer
          value={bizValue}
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
        />
      );
    case CustomPropertyType.MultilineText:
      return (
        <MultilineStringValueRenderer
          value={bizValue}
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
        />
      );
    case CustomPropertyType.SingleChoice:
      return (
        <ChoiceValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
        />
      );
    case CustomPropertyType.MultipleChoice:
      return (
        <ChoiceValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          multiple
        />
      );
    case CustomPropertyType.Number:
      return (
        <NumberValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
        />
      );
    case CustomPropertyType.Percentage:
      return (
        <NumberValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          suffix={'%'}
        />
      );
    case CustomPropertyType.Rating:
      return (
        <RatingValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          allowHalf
          value={bizValue}
        />
      );
    case CustomPropertyType.Boolean:
      return (
        <BooleanValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
    case CustomPropertyType.Link:
      return (
        <LinkValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
    case CustomPropertyType.Attachment:
      return (
        <AttachmentValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
    case CustomPropertyType.Date:
      return (
        <DateTimeValueRenderer
          as={'date'}
          editable={editable}
          format={'YYYY-MM-DD'}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
    case CustomPropertyType.DateTime:
      return (
        <DateTimeValueRenderer
          as={'datetime'}
          format={'YYYY-MM-DD HH:mm:ss'}
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
    case CustomPropertyType.Time:
      return (
        <TimeValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
          format={'HH:mm:ss'}
        />
      );
    case CustomPropertyType.Formula:
      return (
        <FormulaValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
    case CustomPropertyType.Multilevel:
      return (
        <MultilevelValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
    case CustomPropertyType.Tags:
      return (
        <TagsValueRenderer
          editable={editable}
          onValueChange={onValueChange}
          variant={variant}
          value={bizValue}
        />
      );
  }
};
