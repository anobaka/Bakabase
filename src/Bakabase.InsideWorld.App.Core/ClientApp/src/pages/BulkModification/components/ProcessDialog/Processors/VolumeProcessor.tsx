import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Checkbox, DatePicker2, Input, NumberPicker, Select } from '@alifd/next';
import type { IVariable } from '../../Variables';
import TextProcessor from '@/pages/BulkModification/components/ProcessDialog/Processors/TextProcessor';

interface IProps {
  variables: IVariable[];
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
  operation?: Operation;
  properties?: VolumeProperty[];
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
      case Operation.Modify:
        components.push({
          label: 'Target properties',
          comp: (
            <Checkbox.Group
              onChange={v => setValue({
                ...value,
                properties: v.map(x => parseInt(x, 10) as VolumeProperty),
              })}
              dataSource={[
              {
                label: t('Name'),
                value: VolumeProperty.Name,
              },
              {
                label: t('Title'),
                value: VolumeProperty.Title,
              },
            ]}
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

  const renderPropertyProcessors = () => {
    const properties = value.properties || [];
    const processors = properties.map(p => {
      return (
        <div className={'container'}>
          <div className={'title'}>{t(VolumeProperty[p])}</div>
          <TextProcessor variables={variables} />
        </div>
      );
    });
    return processors;
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
      {renderPropertyProcessors()}
    </>
  );
};
