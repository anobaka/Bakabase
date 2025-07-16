'use client';

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Checkbox, Select } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import type { IVariable } from '../../Variables';
import TextProcessor, {
  type ITextProcessorValue,
} from '../Processors/TextProcessor';
import { BmVolumeProcessorOperation } from '@/sdk/constants';

interface IProps {
  variables: IVariable[];
  onChange?: (value: IValue) => any;
  value?: IValue;
}

enum Operation {
  Modify = 1,
  Remove,
}

enum VolumeProperty {
  Name = 1,
  Title,
}

interface IValue {
  operation?: BmVolumeProcessorOperation;
  propertyModifications?: Record<VolumeProperty, ITextProcessorValue>;
}

const Editor = ({
                  variables: propsVariables,
                  value: propsValue,
                  onChange,
                }: IProps) => {
  const { t } = useTranslation();
  const [variables, setVariables] = useState<IVariable[]>(propsVariables || []);
  const [value, setValue] = useState<IValue>(propsValue ?? {});

  const operationDataSource = Object.keys(BmVolumeProcessorOperation).filter(k => Number.isNaN(parseInt(k, 10))).map(x => ({
    label: t<string>(x),
    value: BmVolumeProcessorOperation[x],
  }));

  const selectedProperties = Object.keys(value.propertyModifications || {}).map(k => parseInt(k, 10) as VolumeProperty);

  const changeValue = (patches: Record<any, any>) => {
    setValue({
      ...value,
      ...patches,
    });
  };

  useUpdateEffect(() => {
    if (onChange) {
      onChange(value);
    }
  }, [value]);
  const renderValueComp = () => {
    const components: { label: string; comp: any }[] = [];
    switch (value.operation) {
      case BmVolumeProcessorOperation.Modify:
        components.push({
          label: t<string>('Target properties'),
          comp: (
            <Checkbox.Group
              onChange={v => {
                const newProperties = v.map(p => parseInt(p, 10) as VolumeProperty);
                const properties = value.propertyModifications || {};
                newProperties.forEach(p => {
                  if (!properties[p]) {
                    properties[p] = undefined;
                  }
                });
                selectedProperties.forEach(p => {
                  if (!newProperties.includes(p)) {
                    delete properties[p];
                  }
                });
                changeValue({
                  propertyModifications: properties,
                });
              }}
              dataSource={[
                {
                  label: t<string>('Name'),
                  value: VolumeProperty.Name,
                },
                {
                  label: t<string>('Title'),
                  value: VolumeProperty.Title,
                },
              ]}
              value={Object.keys(value.propertyModifications || {}).map(p => parseInt(p, 10) as VolumeProperty)}
            />
          ),
        });
        break;
      case BmVolumeProcessorOperation.Remove:
        break;
    }
    return components.map((c, i) => {
      return (
        <div className="block" key={i}>
          <div className="label">{c.label}</div>
          <div className="value">{c.comp}</div>
        </div>
      );
    });
  };

  const renderPropertyProcessors = () => {
    const properties = Object.keys(value.propertyModifications ?? {}).map(k => parseInt(k, 10) as VolumeProperty);
    return properties.map(p => {
      return (
        <div className={'container'} key={p}>
          <div className={'title'}>{t<string>(VolumeProperty[p])}</div>
          <TextProcessor.Editor
            variables={variables}
            value={value.propertyModifications?.[p]}
            onChange={v => changeValue({ propertyModifications: { ...value.propertyModifications, [p]: v } })}
          />
        </div>
      );
    });
  };

  return (
    <>
      <div className="block">
        <div className={'label'}>
          {t<string>('Operation')}
        </div>
        <div className="value">
          <Select
            style={{ width: 300 }}
            dataSource={operationDataSource}
            value={value.operation}
            onChange={v => setValue({
              operation: v,
            })}
          />
        </div>
      </div>
      {renderValueComp()}
      {renderPropertyProcessors()}
    </>
  );
};

const Demonstrator = ({
                        value,
                      }: { value: IValue }) => {
  const { t } = useTranslation();
  const [valueTexts, setValueTexts] = useState<string[]>([]);

  switch (value?.operation) {
    case BmVolumeProcessorOperation.Remove:
      return (
        <>
          <div className="primary">{t<string>('Remove')}</div>
        </>
      );
    case BmVolumeProcessorOperation.Modify:
      return (
        <div className={'multiple'}>
          {Object.keys(value.propertyModifications || {}).map(k => parseInt(k, 10) as VolumeProperty).map(p => {
            const textProcessorValue = value.propertyModifications?.[p];
            return (
              <div key={p} className={'line'}>
                {t<string>('Modify')}
                <div className={'primary'}>{t<string>(VolumeProperty[p])}</div>
                {textProcessorValue ? (
                  <TextProcessor.Demonstrator value={textProcessorValue} />
                ) : t<string>('Not set')}
              </div>
            );
          })}
        </div>
      );
    default:
      return (
        <>
          {t<string>('Unsupported value')}
        </>
      );
  }
};

export default class VolumeProcessor {
  static Editor = Editor;
  static Demonstrator = Demonstrator;
}
