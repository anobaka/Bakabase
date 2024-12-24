'use strict';

import { useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import { QuestionCircleOutlined } from '@ant-design/icons';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
import { Button, Dropdown, DropdownItem, DropdownMenu, DropdownTrigger, Select, Tooltip } from '@/components/bakaui';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';

type Props = {
  onChange?: (valueType: BulkModificationProcessorValueType, value?: string) => any;
  variables?: BulkModificationVariable[];
  valueTypes?: BulkModificationProcessorValueType[];
  property: IProperty;
  value?: string;
};

const log = buildLogger('ValueWithMultipleTypeEditor');

const ValueTypeTipsMap: {[key in BulkModificationProcessorValueType]?: string[]} = {
  [BulkModificationProcessorValueType.Dynamic]: [
    'Dynamic value, will change according to the changes in dynamic properties (single choice, multiple choice, multilevel data, etc.).',
    'For example, if you use the \'actor\' from the multiple-choice data as the change result, then in the future, if you modify \'actor\' to \'actor1\', the current data will also be changed to \'actor1\' after processing. However, if you choose a static value, the current data will be changed to \'actor\' after processing.',
    'For non-dynamic properties (such as text, numbers, dates, etc.), there is no difference between setting dynamic values and static values.',
  ],
};

export default (props: Props) => {
  const {
    onChange,
    valueTypes = [BulkModificationProcessorValueType.Static],
    variables,
    property,
    value: propsValue,
  } = props;
  const { t } = useTranslation();
  const [valueType, setValueType] = useState<BulkModificationProcessorValueType>(BulkModificationProcessorValueType.Static);
  const [value, setValue] = useState<string | undefined>(propsValue);

  log(props, valueType, value);

  const renderEditor = () => {
    switch (valueType) {
      case BulkModificationProcessorValueType.Static:
        return (
          <PropertyValueRenderer
            property={property}
            bizValue={value}
            // variant={'light'}
            onValueChange={(dbValue, bizValue) => { onChange?.(valueType, bizValue); }}
          />
        );
      case BulkModificationProcessorValueType.Dynamic:
        return (
          <PropertyValueRenderer
            property={property}
            dbValue={value}
            // variant={'light'}
            onValueChange={(dbValue, bizValue) => { onChange?.(valueType, dbValue); }}
          />
        );
      case BulkModificationProcessorValueType.Variable:
        if (variables && variables.length > 0) {
          return (
            <Select
              size={'sm'}
              dataSource={variables?.map(v => ({
                label: v.name,
                value: v.key,
              }))}
              selectedKeys={value ? new Set([value]) : new Set()}
              onSelectionChange={keys => {
                const key = Array.from(keys)[0] as string;
                setValue(key);
                onChange?.(valueType, key);
              }}
              multiple={false}
            />
          );
        }
        return t('No variables available');
    }
  };

  return (
    <div className={'flex items-center gap-1'}>
      {valueTypes.length > 1 && (
        <Dropdown>
          <DropdownTrigger>
            <Button
              size={'sm'}
              variant="bordered"
            >
              {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[valueType]}`)}
            </Button>
          </DropdownTrigger>
          <DropdownMenu
            selectionMode={'single'}
            aria-label="Static Actions"
            onSelectionChange={keys => {
              const vt = parseInt(Array.from(keys)[0] as string, 10) as BulkModificationProcessorValueType;
              setValue(undefined);
              setValueType(vt);

              onChange?.(vt, undefined);
            }}
          >
            {valueTypes.map(v => {
              const tips = ValueTypeTipsMap[v];
              return (
                <DropdownItem key={v.toString()}>
                  <div className={'flex items-center gap-2'}>
                    {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[v]}`)}
                    {tips && (
                      <Tooltip
                        color={'secondary'}
                        content={(
                          <div className={'flex flex-col gap-1'}>
                            {tips.map(tip => {
                              return (
                                <div>
                                  {t(tip)}
                                </div>
                              );
                            })}
                          </div>
                        )}
                        className={'max-w-[400px]'}
                      >
                        <QuestionCircleOutlined className={'text-base'} />
                      </Tooltip>
                    )}
                  </div>
                </DropdownItem>
              );
            })}
          </DropdownMenu>
        </Dropdown>
      )}
      {renderEditor()}
    </div>
  );
};
