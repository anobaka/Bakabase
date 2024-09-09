import type { Dayjs } from 'dayjs';
import type dayjs from 'dayjs';
import type { Duration } from 'dayjs/plugin/duration';
import StringValueRenderer from './Renderers/StringValueRenderer';
import NumberValueRenderer from './Renderers/NumberValueRenderer';
import BooleanValueRenderer from './Renderers/BooleanValueRenderer';
import LinkValueRenderer from './Renderers/LinkValueRenderer';
import DateTimeValueRenderer from './Renderers/DateTimeValueRenderer';
import { CustomPropertyType, StandardValueType } from '@/sdk/constants';
import type { LinkValue, TagValue } from '@/components/StandardValue/models';
import { MultilevelValueRenderer, TagsValueRenderer, TimeValueRenderer } from '@/components/StandardValue';

type Props = {
  value?: any;
  type: StandardValueType;
  variant?: 'default' | 'light';
  customPropertyType?: CustomPropertyType;
};

const StandardValueRenderer = ({ value, type, variant = 'default', customPropertyType }: Props) => {
  console.log(value, StandardValueType[type]);

  switch (type) {
    case StandardValueType.String:
      return (
        <StringValueRenderer
          value={value as string}
          variant={variant}
          multiline={customPropertyType == CustomPropertyType.MultilineText}
        />
      );
    case StandardValueType.ListString: {
      return (
        <TagsValueRenderer
          value={(value as string[])?.map<TagValue>(v => ({ name: v }))}
          variant={variant}
        />
      );
    }
    case StandardValueType.Decimal:
      return (
        <NumberValueRenderer
          value={value as number}
          variant={variant}
          as={(customPropertyType == undefined || customPropertyType == CustomPropertyType.Rating || customPropertyType == CustomPropertyType.Number) ? 'number' : 'progress'}
        />
      );
    case StandardValueType.Link:
      return (
        <LinkValueRenderer
          value={value as LinkValue}
          variant={variant}
        />
      );
    case StandardValueType.Boolean:
      return (
        <BooleanValueRenderer
          value={value as boolean}
          variant={variant}
        />
      );
    case StandardValueType.DateTime: {
      return (
        <DateTimeValueRenderer
          value={value as dayjs.Dayjs}
          format={(customPropertyType == undefined || customPropertyType == CustomPropertyType.DateTime) ? 'YYYY-MM-DD HH:mm:ss' : 'YYYY-MM-DD'}
          variant={variant}
          as={(customPropertyType == undefined || customPropertyType == CustomPropertyType.DateTime) ? 'datetime' : 'date'}
        />
      );
    }
    case StandardValueType.Time: {
      return (
        <TimeValueRenderer
          value={value as Duration}
          format={'HH:mm:ss'}
          variant={variant}
        />
      );
    }
    case StandardValueType.ListListString: {
      return (
        <MultilevelValueRenderer
          value={value as string[][]}
          variant={variant}
        />
      );
    }
    case StandardValueType.ListTag:
      return (
        <TagsValueRenderer
          value={value as TagValue[]}
          variant={variant}
        />
      );
  }
};

export default StandardValueRenderer;
