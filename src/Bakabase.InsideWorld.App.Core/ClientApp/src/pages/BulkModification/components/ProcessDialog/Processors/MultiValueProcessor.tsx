import React, { useEffect, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { Button, Checkbox, DatePicker2, Input, NumberPicker, Radio, Select } from '@alifd/next';
import { useUpdateEffect } from 'react-use';
import type { IVariable } from '../../Variables';
import TextProcessor from '@/pages/BulkModification/components/ProcessDialog/Processors/TextProcessor';
import BApi from '@/sdk/BApi';

interface IProps {
  variables: IVariable[];
  getCandidates: () => Promise<{label: string; value: number}[]>;
  onChange?: (value: IMultiValueProcessorValue) => any;
}

enum Operation {
  Add = 1,
  Remove,
  SetWithFixedValue,
  Modify,
}

enum FilterBy {
  All = 1,
  Containing = 2,
  Matching,
}

export interface IMultiValueProcessorValue {
  operation?: Operation;
  value?: any[];
  filterBy?: FilterBy;
  find?: string;
  replace?: string;
}

const Editor = ({ variables: propsVariables, getCandidates, onChange }: IProps) => {
  const { t } = useTranslation();
  const [variables, setVariables] = useState<IVariable[]>(propsVariables || []);
  const [value, setValue] = useState<IMultiValueProcessorValue>({});

  const [candidates, setCandidates] = useState<{label: string; value: number}[]>([]);

  const operationDataSource = Object.keys(Operation).filter(k => Number.isNaN(parseInt(k, 10))).map(x => ({
    label: t(x),
    value: Operation[x],
  }));

  const removeByDataSource = Object.keys(FilterBy).filter(k => Number.isNaN(parseInt(k, 10))).map(x => ({
    label: t(x),
    value: FilterBy[x],
  }));

  useEffect(() => {
    getCandidates().then(r => setCandidates(r));
  }, []);

  useUpdateEffect(() => {
    if (onChange) {
      onChange(value);
    }
  }, [value]);

  const changeValue = (patches: Record<any, any>) => {
    setValue({
      ...value,
      ...patches,
    });
  };

  const renderValueComp = () => {
    const componentsData: { label: string; comp: any }[] = [];
    switch (value.operation) {
      case Operation.Add:
      case Operation.SetWithFixedValue:
        componentsData.push({
          label: t('Value'),
          comp: (
            <Select
              dataSource={candidates}
              mode={'tag'}
              autoWidth
              style={{ width: '90%' }}
              onChange={value => changeValue({ value })}
            />
          ),
        });
        break;
      case Operation.Remove:
      case Operation.Modify:
        componentsData.push({
          label: t('Filter'),
          comp: (
            <Radio.Group
              dataSource={removeByDataSource}
              onChange={filterBy => changeValue({ filterBy })}
            />
          ),
        });
        if (value.filterBy == FilterBy.Matching || value.filterBy == FilterBy.Containing) {
          componentsData.push({
            label: t('Value'),
            comp: (
              <Input
                onChange={find => changeValue({ find })}
              />
            ),
          });
        }
        break;
    }
    const components = componentsData.map(c => {
      return (
        <div className="block">
          <div className="label">{c.label}</div>
          <div className="value">{c.comp}</div>
        </div>
      );
    });

    if (value.operation == Operation.Modify) {
      components.push(
        <TextProcessor.Editor variables={variables} />,
      );
    }
    return components;
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

const Demonstrator = ({ value, getDataSource }: {value: IMultiValueProcessorValue; getDataSource: (keys: any[]) => Promise<string[]>}) => {
  const { t } = useTranslation();
  const [valueTexts, setValueTexts] = useState<string[]>([]);

  useEffect(() => {
    getDataSource(value.value!).then(r => setValueTexts(r));
  }, [getDataSource]);

  switch (value.operation) {
    case Operation.Remove:
      return (
        <>
          <div className="primary">{t('Remove')}</div>
        </>
      );
    case Operation.Add:
      return (
        <>
          <div className="primary">{t('Add')}</div>
          {valueTexts.map((t, i) => {
            return (
              <React.Fragment key={i}>
                <div className="secondary">{t}</div>
                {i < valueTexts.length - 1 && ','}
              </React.Fragment>
            );
          })}
        </>
      );
    case Operation.SetWithFixedValue:
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.SetWithFixedValue'}
          >
            <div className="primary" />
            with fixed value
          </Trans>
          <div className="secondary">{valueText}</div>
        </>
      );
    case Operation.Modify:
      return (
        <>
          {/* <Trans */}
          {/*   i18nKey={'BulkModification.Processor.Demonstrator.Operation.Replace'} */}
          {/*   values={{ */}
          {/*     find: value.find, */}
          {/*     replace: value.replace, */}
          {/*   }} */}
          {/* > */}
          {/*   <div className="primary" /> */}
          {/*   <div className={'secondary'} /> */}
          {/* </Trans> */}
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

export default class MultiValueProcessor {
  static Editor = Editor;
  static Demonstrator = Demonstrator;
}
