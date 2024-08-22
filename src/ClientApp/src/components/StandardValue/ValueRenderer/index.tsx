import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import type { Duration } from 'dayjs/plugin/duration';
import StringValueRenderer from './Renderers/StringValueRenderer';
import NumberValueRenderer from './Renderers/NumberValueRenderer';
import BooleanValueRenderer from './Renderers/BooleanValueRenderer';
import LinkValueRenderer from './Renderers/LinkValueRenderer';
import DateTimeValueRenderer from './Renderers/DateTimeValueRenderer';
import { StandardValueType } from '@/sdk/constants';
import type { LinkValue, TagValue } from '@/components/StandardValue/models';
import { MultilevelValueRenderer, TagsValueRenderer, TimeValueRenderer } from '@/components/StandardValue';

type Props = {
  value?: any;
  type: StandardValueType;
  variant?: 'default' | 'light';
};

const StandardValueRenderer = ({ value, type, variant = 'default' }: Props) => {
  console.log(value, StandardValueType[type]);

  switch (type) {
    case StandardValueType.String:
      return (
        <StringValueRenderer
          value={value as string}
          variant={variant}
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
      const stringDateTime = value as string;
      let date: Dayjs | undefined;
      if (stringDateTime != undefined && stringDateTime.length > 0) {
        date = dayjs(stringDateTime);
      }
      return (
        <DateTimeValueRenderer
          value={date}
          format={'YYYY-MM-DD HH:mm:ss'}
          variant={variant}
          as={'datetime'}
        />
      );
    }
    case StandardValueType.Time: {
      const stringTime = value as string;
      let time: Duration | undefined;
      if (stringTime != undefined && stringTime.length > 0) {
        time = dayjs.duration(stringTime);
      }
      return (
        <TimeValueRenderer
          value={time}
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
