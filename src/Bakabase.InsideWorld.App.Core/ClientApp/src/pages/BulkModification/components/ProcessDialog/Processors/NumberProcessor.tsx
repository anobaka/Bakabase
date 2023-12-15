import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DatePicker2, Input, NumberPicker, Select } from '@alifd/next';
import type { IVariable } from '../../Variables';

interface IProps {
  variables: IVariable[];
}

enum Operation {
  SetWithFixedValue = 1,
  SetWithCustomValue,
  Remove,
}

interface IValue {
  operation?: Operation;
  value?: string;
}

export default ({ variables: propsVariables }: IProps) => {
  const { t } = useTranslation();
  const [variables, setVariables] = useState<IVariable[]>(propsVariables || []);
  const [value, setValue] = useState<IValue>({});

  const operationDataSource = Object.keys(Operation).filter(k => Number.isNaN(parseInt(k, 10))).map(x => ({
    label: t(x),
    value: Operation[x],
  }));

  const renderValueComp = () => {
    const components: { label: string; comp: any }[] = [];
    switch (value.operation) {
      case Operation.SetWithFixedValue:
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
      case Operation.SetWithCustomValue:
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
      case Operation.Remove:
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
