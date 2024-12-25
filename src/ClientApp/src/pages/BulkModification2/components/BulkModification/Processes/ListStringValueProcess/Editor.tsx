import { useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import { StringValueProcessEditor } from '../StringValueProcess';
import type { ListStringValueProcessOptions } from './models';
import { validate } from './helpers';
import {
  BulkModificationListStringProcessOperation,
  bulkModificationListStringProcessOperations,
  bulkModificationProcessorOptionsItemsFilterBies,
  BulkModificationProcessorOptionsItemsFilterBy,
  type BulkModificationProcessorValueType,
  PropertyPool,
  PropertyType,
  StandardValueType,
} from '@/sdk/constants';
import { Input, Select } from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { buildLogger } from '@/components/utils';
import type { RecursivePartial } from '@/components/types';


type Props = {
  property: IProperty;
  operation?: BulkModificationListStringProcessOperation;
  options?: ListStringValueProcessOptions;
  variables?: BulkModificationVariable[];
  availableValueTypes?: BulkModificationProcessorValueType[];
  onChange?: (operation: BulkModificationListStringProcessOperation, options: ListStringValueProcessOptions, error?: string) => any;
};

const log = buildLogger('ListProcessorEditor');


export default ({
                  property,
                  operation: propsOperation,
                  options: propsOptions,
                  onChange,
                  variables,
                  availableValueTypes,
                }: Props) => {
  const { t } = useTranslation();
  const [options, setOptions] = useState<RecursivePartial<ListStringValueProcessOptions>>(propsOptions ?? {});
  const [operation, setOperation] = useState<BulkModificationListStringProcessOperation>(propsOperation ?? BulkModificationListStringProcessOperation.SetWithFixedValue);

  log('operation', operation, 'options', options, typeof operation);

  useEffect(() => {
    const error = validate(operation, options);
    onChange?.(operation, options as ListStringValueProcessOptions, error);
  }, [options, operation]);

  const changeOptions = (patches: RecursivePartial<ListStringValueProcessOptions>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    setOptions(newOptions);
  };

  const changeOperation = (newOperation: BulkModificationListStringProcessOperation) => {
    setOperation(newOperation);
    const newOptions = {};
    setOptions(newOptions);
  };

  const renderValueCell = (field: string = 'value') => {
    return (
      <ProcessValue
        valueTypes={availableValueTypes}
        value={options[field]}
        onChange={(valueType, value) => changeOptions({
          [field]: value,
          valueType,
        })}
        variables={variables}
        property={property}
      />
    );
  };

  const renderSubOptions = (options: RecursivePartial<ListStringValueProcessOptions>) => {
    log('renderOptions', operation, options);

    if (!operation) {
      return null;
    }
    const components: { label: string; comp: any }[] = [];
    switch (operation) {
      case BulkModificationListStringProcessOperation.SetWithFixedValue:
      case BulkModificationListStringProcessOperation.Append:
      case BulkModificationListStringProcessOperation.Prepend:
        components.push({
          label: t('Value'),
          comp: renderValueCell(),
        });
        break;
      case BulkModificationListStringProcessOperation.Modify: {
        const filterBy = options?.modifyOptions?.filterBy;
        components.push({
          label: t('Filter by'),
          comp: (
            <div className={'flex items-center gap-2'}>
              <div className={'w-2/5'}>
                <Select
                  dataSource={bulkModificationProcessorOptionsItemsFilterBies.map(fb => ({
                    label: t(fb.label),
                    value: fb.value,
                  }))}
                  selectionMode={'single'}
                  onSelectionChange={keys => {
                    changeOptions({
                      modifyOptions: {
                        ...options?.modifyOptions,
                        filterBy: parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationProcessorOptionsItemsFilterBy,
                      },
                    });
                  }}
                />
              </div>
              {filterBy != undefined && (() => {
                switch (filterBy) {
                  case BulkModificationProcessorOptionsItemsFilterBy.All:
                    return null;
                  case BulkModificationProcessorOptionsItemsFilterBy.Containing:
                  case BulkModificationProcessorOptionsItemsFilterBy.Matching:
                    return (
                      <Input
                        placeholder={t(filterBy == BulkModificationProcessorOptionsItemsFilterBy.Containing ? 'Please input keyword' : 'Please input a regular expression')}
                        onValueChange={v => {
                          changeOptions({
                            modifyOptions: {
                              ...options?.modifyOptions,
                              filterValue: v,
                            },
                          });
                        }}
                      />
                    );
                }
              })()}
            </div>
          ),
        });
        components.push({
          label: t('For each filtered item'),
          comp: (
            <StringValueProcessEditor
              options={options?.modifyOptions?.options}
              operation={options?.modifyOptions?.operation}
              property={{
                type: PropertyType.SingleLineText,
                bizValueType: StandardValueType.String,
                dbValueType: StandardValueType.String,
                id: 0,
                name: 'Fake text',
                pool: PropertyPool.Custom,
                poolName: 'Custom',
                typeName: 'SingleLineText',
              }}
              onChange={(sOperation, sOptions, error) => {
                changeOptions({
                  modifyOptions: {
                    ...options?.modifyOptions,
                    operation: sOperation,
                    options: sOptions,
                  },
                });
              }}
              variables={variables}
              availableValueTypes={availableValueTypes}
            />
          ),
        });
        break;
      }
      case BulkModificationListStringProcessOperation.Delete:
        break;
    }
    return components.map((c, i) => {
      return (
        <>
          <div>{c.label}</div>
          <div>{c.comp}</div>
        </>
      );
    });
  };

  return (
    <div className={'grid items-center gap-2'} style={{ gridTemplateColumns: 'auto minmax(0, 1fr)' }}>
      <div>
        {t('Operation')}
      </div>
      <Select
        dataSource={bulkModificationListStringProcessOperations.map(tpo => ({
          label: t(tpo.label),
          value: tpo.value,
        }))}
        selectionMode={'single'}
        selectedKeys={operation == undefined ? undefined : [operation.toString()]}
        onSelectionChange={keys => {
          changeOperation(parseInt(Array.from(keys || [])[0] as string, 10) as BulkModificationListStringProcessOperation);
        }}
      />
      {renderSubOptions(options)}
    </div>
  );
};
