import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import type { Duration } from 'dayjs/plugin/duration';
import React from 'react';
import type { DataPool } from '../../../models';
import type { FilterValueContext } from './models';
import { RenderType } from './models';
import { NumberValueRenderer, StringValueRenderer, BooleanValueRenderer, ListStringValueRenderer, DateTimeValueRenderer } from './ValueRenderer';
import {
  BooleanValueEditor,
  ChoiceValueEditor,
  DateTimeValueEditor,
  NumberValueEditor,
  StringValueEditor, TimeValueEditor,
} from './ValueEditor';
import type { ChoicePropertyOptions, IProperty, MultilevelPropertyOptions } from '@/components/Property/models';
import { CustomPropertyType, ResourceProperty } from '@/sdk/constants';
import { findNodeChainInMultilevelData } from '@/components/StandardValue/helpers';
import MultilevelValueEditor
  from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/FilterValue/ValueEditor/Editors/MultilevelValueEditor';
import type { MultilevelData } from '@/components/StandardValue/models';

export function getRenderType(property?: IProperty): RenderType | undefined {
  if (!property) {
    return;
  }
  let rt: RenderType | undefined;
  if (property.isReserved) {
    const type = property.id as ResourceProperty;
    switch (type) {
      case ResourceProperty.FileName:
      case ResourceProperty.DirectoryPath:
      case ResourceProperty.CreatedAt:
      case ResourceProperty.FileCreatedAt:
      case ResourceProperty.FileModifiedAt:
      {
        return RenderType.StringValue;
      }
      case ResourceProperty.Category:
        break;
      case ResourceProperty.MediaLibrary:
        return RenderType.MediaLibrary;
    }
  } else {
    switch (property.type as CustomPropertyType) {
      case CustomPropertyType.SingleLineText:
      case CustomPropertyType.MultilineText:
        return RenderType.StringValue;
      case CustomPropertyType.SingleChoice:
        return RenderType.ChoiceValue;
      case CustomPropertyType.MultipleChoice:
        return RenderType.MultipleChoiceValue;
      case CustomPropertyType.Number:
      case CustomPropertyType.Percentage:
      case CustomPropertyType.Rating:
        return RenderType.NumberValue;
      case CustomPropertyType.Boolean:
        return RenderType.BooleanValue;
        break;
      case CustomPropertyType.Link:
        return RenderType.StringValue;
      case CustomPropertyType.Attachment:
        break;
      case CustomPropertyType.Date:
        return RenderType.DateValue;
      case CustomPropertyType.DateTime:
        return RenderType.DateTimeValue;
      case CustomPropertyType.Time:
        return RenderType.TimeValue;
      case CustomPropertyType.Formula:
        break;
      case CustomPropertyType.Multilevel:
        return RenderType.MultilevelValue;
    }
  }
  return rt;
}


export function buildFilterValueContext(property?: IProperty, value?: string, dataPool?: DataPool): FilterValueContext | undefined {
  const jo = value === null || value === undefined ? null : JSON.parse(value);


  const serialize = (v: any) => (v == undefined ? undefined : JSON.stringify(v));

  switch (ctxType) {
    case RenderType.StringValue: {
      const typedValue = jo as string;
      return {
        ValueRenderer: (props) => (<StringValueRenderer
          value={typedValue}
          {...props}
        />),
        ValueEditor: React.memo(({ onChange, ...props }) => (<StringValueEditor
          initValue={typedValue}
          onChange={v => onChange?.(serialize(v))}
          {...props}
        />)),
      };
    }
    case RenderType.MediaLibrary:
    {
      const typedValue = jo as number[];

      const categoryMap = dataPool?.categoryMap || {};
      const mediaLibraryMap = dataPool?.mediaLibraryMap || {};

      const displayValue: string[] = [];
      for (const id of typedValue) {
        const library = mediaLibraryMap[id];
        if (library) {
          const category = categoryMap[library.categoryId];
          displayValue.push(`[${category?.name}]${library.name}(${library.resourceCount})`);
        }
      }

      return {
        ValueRenderer: (props) => <ListStringValueRenderer value={displayValue} {...props} />,
        ValueEditor: ({ onChange, ...props }) => (<MultilevelValueEditor
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
          {...props}
        />) };
    }
    case RenderType.ChoiceValue: {
      const typedValue = jo as string;
      const displayValue = property.options?.choices?.find((c) => c.value === typedValue)?.label;
      return {
        ValueRenderer: () => (<StringValueRenderer value={displayValue} />),
        ValueEditor: () => (<ChoiceValueEditor
          initValue={typedValue == undefined || typedValue.length == 0 ? undefined : [typedValue]}
          multiple={false}
          getDataSource={async () => {
            const options = property.options as ChoicePropertyOptions;
            return options.choices?.map(c => ({ id: c.id!, value: c.value! }));
          }}
        />),
      };
    }
    case RenderType.MultipleChoiceValue: {
      const typedValue = jo as string[];
      const displayValue = typedValue
        .map(v => property.options?.choices?.find((c) => c.value === v)?.label)
        .filter(x => x !== undefined && x?.length > 0);
      return {
        ValueRenderer: (props) => (<ListStringValueRenderer
          value={displayValue}
          {...props}
        />),
        ValueEditor: ({ onChange, ...props }) => (<ChoiceValueEditor
          initValue={typedValue}
          multiple
          getDataSource={async () => {
            const options = property.options as ChoicePropertyOptions;
            return options.choices?.map(c => ({ id: c.id!, value: c.value! }));
          }}
          onChange={v => onChange?.(serialize(v))}
          {...props}
        />) };
    }
    case RenderType.MultilevelValue:
    {
      const typedValue = jo as string[];
      const options = property.options as MultilevelPropertyOptions;
      const displayValue = typedValue
        .map(v => findNodeChainInMultilevelData(options.data || [], v)?.map(n => n.label).join(':'))
        .filter(x => x !== undefined && x?.length > 0) as string[];
      return {
        ValueRenderer: (props) => (<ListStringValueRenderer
          value={displayValue}
          {...props}
        />),
        ValueEditor: ({ onChange, ...props }) => (<MultilevelValueEditor
          initValue={typedValue}
          getDataSource={async () => { return options.data; }}
          onChange={v => onChange?.(serialize(v))}
          {...props}
        />) };
    }
    case RenderType.NumberValue: {
      const typedValue = jo as number;
      return {
        ValueRenderer: (props) => (<NumberValueRenderer
          value={typedValue}
          {...props}
        />),
        ValueEditor: ({ onChange, ...props }) => (<NumberValueEditor
          initValue={typedValue}
          onChange={v => onChange?.(serialize(v))}
          {...props}
        />),
      };
    }
    case RenderType.BooleanValue: {
      const typedValue = jo as boolean;
      return {
        ValueRenderer: (props) => (<BooleanValueRenderer
          value={typedValue}
          {...props}
        />),
        ValueEditor: ({ onChange, ...props }) => (<BooleanValueEditor
          initValue={typedValue}
          onChange={v => onChange?.(serialize(v))}
          {...props}
        />),
      };
    }
    case RenderType.DateValue:
    {
      const stringDateTime = jo as string;
      let date: Dayjs | undefined;
      if (stringDateTime != undefined && stringDateTime.length > 0) {
        date = dayjs(stringDateTime);
      }
      return {
        ValueRenderer: (props) => (<DateTimeValueRenderer
          value={date}
          format={'YYYY-MM-DD'}
          {...props}
        />),
        ValueEditor: ({ onChange, ...props }) => (<DateTimeValueEditor initValue={date} mode={'date'} onChange={v => onChange?.(v?.format('YYYY-MM-DD'))} />),
      };
    }
    case RenderType.DateTimeValue:
    {
      const stringDateTime = jo as string;
      let date: Dayjs | undefined;
      if (stringDateTime != undefined && stringDateTime.length > 0) {
        date = dayjs(stringDateTime);
      }
      return {
        ValueRenderer: (props) => (<DateTimeValueRenderer
          value={date}
          format={'YYYY-MM-DD HH:mm:ss'}
          {...props}
        />),
        ValueEditor: ({ onChange, ...props }) => (<DateTimeValueEditor initValue={date} mode={'datetime'} onChange={v => onChange?.(v?.format('YYYY-MM-DD HH:mm:ss'))} />),
      };
    }
    case RenderType.TimeValue:
    {
      const stringTime = jo as string;
      let time: Duration | undefined;
      if (stringTime != undefined && stringTime.length > 0) {
        time = dayjs.duration(stringTime);
      }
      return {
        ValueRenderer: (props) => (<DateTimeValueRenderer
          value={time}
          format={'HH:mm:ss'}
          {...props}
        />),
        ValueEditor: ({ onChange, ...props }) => (<TimeValueEditor initValue={time} onChange={v => onChange?.(v?.format('HH:mm:ss'))} />),
      };
    }
  }

  return;
}
