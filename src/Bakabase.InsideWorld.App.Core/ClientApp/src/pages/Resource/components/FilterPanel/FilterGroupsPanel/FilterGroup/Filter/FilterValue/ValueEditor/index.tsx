import React from 'react';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import type { Duration } from 'dayjs/plugin/duration';
import { getRenderType } from '../helpers';
import BooleanValueEditor from './Editors/BooleanValueEditor';
import ChoiceValueEditor from './Editors/ChoiceValueEditor';
import DateTimeValueEditor from './Editors/DateTimeValueEditor';
import NumberValueEditor from './Editors/NumberValueEditor';
import StringValueEditor from './Editors/StringValueEditor';
import TimeValueEditor from './Editors/TimeValueEditor';
import type { ChoicePropertyOptions, IProperty, MultilevelPropertyOptions } from '@/components/Property/models';
import type { DataPool } from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import {
  RenderType,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/FilterValue/models';
import {
  BooleanValueRenderer, DateTimeValueRenderer,
  ListStringValueRenderer, NumberValueRenderer,
  StringValueRenderer,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/FilterValue/ValueRenderer';
import MultilevelValueEditor
  from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/FilterValue/ValueEditor/Editors/MultilevelValueEditor';
import type { MultilevelData } from '@/components/StandardValue/models';
import { findNodeChainInMultilevelData } from '@/components/StandardValue/helpers';

export {
  StringValueEditor,
  NumberValueEditor,
  BooleanValueEditor,
  ChoiceValueEditor,
  TimeValueEditor,
  DateTimeValueEditor,
};

interface Props {
  value?: any;
  property: IProperty;
  dataPool?: DataPool;
  onChange?: (value?: string) => any;
}

export default ({ value, property, dataPool, onChange }: Props) => {
  const jo = value === null || value === undefined ? null : JSON.parse(value);
  const serialize = (v: any) => (v == undefined ? undefined : JSON.stringify(v));
  const ctxType = getRenderType(property);

  switch (ctxType) {
    case RenderType.StringValue: {
      const typedValue = jo as string;
      return (<StringValueEditor
        initValue={typedValue}
        onChange={v => onChange?.(serialize(v))}
      />);
    }
    case RenderType.MediaLibrary:
    {
      const typedValue = jo as number[];
      const categoryMap = dataPool?.categoryMap || {};
      const mediaLibraryMap = dataPool?.mediaLibraryMap || {};

      return (
        <MultilevelValueEditor
          initValue={typedValue.map(v => v.toString())}
          getDataSource={async () => {
            const multilevelData: MultilevelData<string>[] = [];
            Object.values(categoryMap).forEach((c) => {
              const md: MultilevelData<string> = {
                value: `c-${c.id}`,
                label: c.name,
                children: Object.values(mediaLibraryMap).map<MultilevelData<string>>(x => ({
                  value: x.id.toString(),
                  label: x.name,
                })),
              };

              multilevelData.push(md);
            });
            return multilevelData;
          }}
          onChange={v => {
            const newValue = v?.map(x => parseInt(x, 10));
            onChange?.(serialize(newValue));
          }}
        />
      );
    }
    case RenderType.ChoiceValue: {
      const typedValue = jo as string;
      return (
        <ChoiceValueEditor
          initValue={typedValue == undefined || typedValue.length == 0 ? undefined : [typedValue]}
          multiple={false}
          getDataSource={async () => {
            const options = property.options as ChoicePropertyOptions;
            return options.choices?.map(c => ({ id: c.id!, value: c.value! }));
          }}
        />
      );
    }
    case RenderType.MultipleChoiceValue: {
      const typedValue = jo as string[];
      return (
        <ChoiceValueEditor
          initValue={typedValue}
          multiple
          getDataSource={async () => {
            const options = property.options as ChoicePropertyOptions;
            return options.choices?.map(c => ({ id: c.id!, value: c.value! }));
          }}
          onChange={v => onChange?.(serialize(v))}
        />
      );
    }
    case RenderType.MultilevelValue:
    {
      const typedValue = jo as string[];
      const options = property.options as MultilevelPropertyOptions;
      return (
        <MultilevelValueEditor
          initValue={typedValue}
          getDataSource={async () => { return options.data; }}
          onChange={v => onChange?.(serialize(v))}
        />
      );
    }
    case RenderType.NumberValue: {
      const typedValue = jo as number;
      return (
        <NumberValueEditor
          initValue={typedValue}
          onChange={v => onChange?.(serialize(v))}
        />
      );
    }
    case RenderType.BooleanValue: {
      const typedValue = jo as boolean;
      return (
        <BooleanValueEditor
          initValue={typedValue}
          onChange={v => onChange?.(serialize(v))}
        />
      );
    }
    case RenderType.DateValue:
    {
      const stringDateTime = jo as string;
      let date: Dayjs | undefined;
      if (stringDateTime != undefined && stringDateTime.length > 0) {
        date = dayjs(stringDateTime);
      }
      return (
        <DateTimeValueEditor
          initValue={date}
          mode={'date'}
          onChange={v => onChange?.(v?.format('YYYY-MM-DD'))}
        />
      );
    }
    case RenderType.DateTimeValue:
    {
      const stringDateTime = jo as string;
      let date: Dayjs | undefined;
      if (stringDateTime != undefined && stringDateTime.length > 0) {
        date = dayjs(stringDateTime);
      }
      return (
        <DateTimeValueEditor
          initValue={date}
          mode={'datetime'}
          onChange={v => onChange?.(v?.format('YYYY-MM-DD HH:mm:ss'))}
        />
      );
    }
    case RenderType.TimeValue:
    {
      const stringTime = jo as string;
      let time: Duration | undefined;
      if (stringTime != undefined && stringTime.length > 0) {
        time = dayjs.duration(stringTime);
      }
      return (
        <TimeValueEditor
          initValue={time}
          onChange={v => onChange?.(v?.format('HH:mm:ss'))}
        />
      );
    }
  }
  return;
};
