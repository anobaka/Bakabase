import React, { useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { DatePicker2, Input, NumberPicker, Select } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import type { IVariable } from '../../Variables';
import { BmSimpleValueProcessorOperation } from '@/sdk/constants';

interface IProps {
  variables: IVariable[];
  onChange?: (value: IValue) => any;
  value?: IValue;
}

// enum Operation {
//   SetWithFixedValue = 1,
//   SetWithDynamicValue,
//   Remove,
// }

interface IValue {
  operation?: BmSimpleValueProcessorOperation;
  value?: string;
}

const Editor = ({ variables: propsVariables, onChange, value: propsValue }: IProps) => {
  const { t } = useTranslation();
  const [variables, setVariables] = useState<IVariable[]>(propsVariables || []);
  const [value, setValue] = useState<IValue>(propsValue ?? {});

  const operationDataSource = Object.keys(BmSimpleValueProcessorOperation).filter(k => Number.isNaN(parseInt(k, 10))).map(x => ({
    label: t(x),
    value: BmSimpleValueProcessorOperation[x],
  }));

  useUpdateEffect(() => {
    if (onChange) {
      onChange(value);
    }
  }, [value]);

  const renderValueComp = () => {
    const components: { label: string; comp: any }[] = [];
    switch (value.operation) {
      case BmSimpleValueProcessorOperation.SetWithFixedValue:
        components.push({
          label: 'Value',
          comp: (
            <NumberPicker
              precision={2}
              value={value.value}
              onChange={v => setValue({
                ...value,
                value: v.toString(),
              })}
            />
          ),
        });
        break;
      case BmSimpleValueProcessorOperation.SetWithDynamicValue:
        components.push({
          label: 'Value',
          comp: (
            <Input
              value={value.value}
              onChange={v => setValue({
                ...value,
                value: v,
              })}
            />
          ),
        });
        break;
      case BmSimpleValueProcessorOperation.Remove:
        break;
    }
    return components.map(c => {
      return (
        <div className="block">
          <div className="label">{c.label}</div>
          <div className="value">{c.comp}</div>
        </div>
      );
    });
  };

  return (
    <>
      <div className="block">
        <div className={'label'}>
          {t('Operation')}
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
    </>
  );
};

const Demonstrator = ({ value }: { value: IValue }) => {
  const { t } = useTranslation();

  // console.log('44444', value);

  switch (value?.operation) {
    case BmSimpleValueProcessorOperation.Remove:
      return (
        <>
          <div className="primary">{t('Remove')}</div>
        </>
      );
    case BmSimpleValueProcessorOperation.SetWithFixedValue: {
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.SetWithFixedValue'}
          >
            <div className="primary" />
            with fixed value
          </Trans>
          <div className="secondary">{value.value}</div>
        </>
      );
    }
    case BmSimpleValueProcessorOperation.SetWithDynamicValue:
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.SetWithDynamicValue'}
          >
            <div className="primary" />
            with dynamic value
          </Trans>
          <div className="secondary">{value.value}</div>
        </>
      );
    default:
      return (
        <>
          {t('Unsupported value')}
        </>
      );
  }
};

export default class NumberProcessor {
  static Editor = Editor;
  static Demonstrator = Demonstrator;
}
