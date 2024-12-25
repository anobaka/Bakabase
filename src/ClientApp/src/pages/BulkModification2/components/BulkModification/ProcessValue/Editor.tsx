'use strict';

import { useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import { ExclamationCircleOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { useUpdateEffect } from 'react-use';
import type {
  BulkModificationProcessValue,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import type {
  StandardValueType } from '@/sdk/constants';
import {
  BulkModificationProcessorValueType,
  bulkModificationProcessorValueTypes,
  PropertyType,
  propertyTypes,
} from '@/sdk/constants';
import { Button, Dropdown, DropdownItem, DropdownMenu, DropdownTrigger, Select, Tooltip } from '@/components/bakaui';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';

type Props = {
  onChange?: (value: BulkModificationProcessValue) => any;
  variables?: BulkModificationVariable[];
  value?: BulkModificationProcessValue;
  valueType: StandardValueType;
  property?: IProperty;
};

const log = buildLogger('ValueWithMultipleTypeEditor');

const ValueSourceTipsMap: { [key in BulkModificationProcessorValueType]?: string[] } = {
  [BulkModificationProcessorValueType.DynamicPropertyDbValue]: [
    'Dynamic value, will change according to the changes in dynamic properties (single choice, multiple choice, multilevel data, etc.).',
    'For example, if you use the \'actor\' from the multiple-choice data as the change result, then in the future, if you modify \'actor\' to \'actor1\', the current data will also be changed to \'actor1\' after processing. However, if you choose a static value, the current data will be changed to \'actor\' after processing.',
    'For non-dynamic properties (such as text, numbers, dates, etc.), there is no difference between setting dynamic values and static values.',
  ],
};

type PropertyTypeForManuallySettingValue = {
  type: PropertyType;
  isAvailable: boolean;
  unavailableReason?: string;
};

const buildDefaultValue = (property?: IProperty): BulkModificationProcessValue => {
  if (property) {
    return {
      type: BulkModificationProcessorValueType.DynamicPropertyDbValue,
      propertyPool: property.pool,
      propertyId: property.id,
    };
  }

  return {
    type: BulkModificationProcessorValueType.ManuallyInput,
  };
};

export default (props: Props) => {
  const {
    onChange,
    variables,
    value: propsValue,
    valueType,
    property,
  } = props;
  const { t } = useTranslation();
  const [value, setValue] = useState<BulkModificationProcessValue>(propsValue ?? buildDefaultValue(property));

  const [propertyTypesForManuallySettingValue, setPropertyTypesForManuallySettingValue] = useState<PropertyTypeForManuallySettingValue[]>([]);

  useEffect(() => {
    if (value.type == BulkModificationProcessorValueType.ManuallyInput && propertyTypesForManuallySettingValue.length == 0) {
      BApi.property.getAvailablePropertyTypesForManuallySettingValue().then(r => {
        setPropertyTypesForManuallySettingValue(r.data || []);
      });
    }
  }, [value.type, propertyTypesForManuallySettingValue]);

  log(props, valueType, value);

  const renderEditor = () => {
    // switch (value.type) {
    //   case BulkModificationProcessorValueType.ManuallyInput:
    //   case BulkModificationProcessorValueType.DynamicPropertyBizValue:
    //     return (
    //       <PropertyValueRenderer
    //         property={property}
    //         dbValue={value}
    //         // variant={'light'}
    //         onValueChange={(dbValue, bizValue) => {
    //           onChange?.(valueType, dbValue);
    //         }}
    //       />
    //     );
    //   case BulkModificationProcessorValueType.DynamicPropertyDbValue:
    //     return (
    //       <PropertyValueRenderer
    //         property={property}
    //         dbValue={value}
    //         // variant={'light'}
    //         onValueChange={(dbValue, bizValue) => {
    //           onChange?.(valueType, dbValue);
    //         }}
    //       />
    //     );
    //   case BulkModificationProcessorValueType.Variable:
    //     if (variables && variables.length > 0) {
    //       return (
    //         <Select
    //           size={'sm'}
    //           dataSource={variables?.map(v => ({
    //             label: v.name,
    //             value: v.key,
    //           }))}
    //           selectedKeys={value ? new Set([value]) : new Set()}
    //           onSelectionChange={keys => {
    //             const key = Array.from(keys)[0] as string;
    //             setValue(key);
    //             onChange?.(valueType, key);
    //           }}
    //           multiple={false}
    //         />
    //       );
    //     }
    //     return t('No variables available');
    // }
  };

  return (
    <div className={'flex items-center gap-1'}>
      <Dropdown>
        <DropdownTrigger>
          <Button
            size={'sm'}
            variant="bordered"
          >
            {(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[value.type]}`)}
          </Button>
        </DropdownTrigger>
        <DropdownMenu
          selectionMode={'single'}
          aria-label="Static Actions"
          onSelectionChange={keys => {
            const source = parseInt(Array.from(keys)[0] as string, 10) as BulkModificationProcessorValueType;
            const nv = {
              ...value,
              source,
            };
            setValue(nv);
            onChange?.(nv);
          }}
        >
          {bulkModificationProcessorValueTypes.map(s => {
            const tips = ValueSourceTipsMap[s.value];
            return (
              <DropdownItem key={s.value}>
                <div className={'flex items-center gap-2'}>
                  {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[s]}`)}
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
      {value.type == BulkModificationProcessorValueType.ManuallyInput && (
        <Dropdown>
          <DropdownTrigger>
            <Button
              size={'sm'}
              variant="bordered"
            >
              {value.propertyType ? (
                `${t('Use {{any}}', { any: `${t(PropertyType[value.propertyType])} ${t('Editor')}` })}`
              ) : t('Choose a property value editor')}
            </Button>
          </DropdownTrigger>
          <DropdownMenu
            selectionMode={'single'}
            aria-label="Static Actions"
            onSelectionChange={keys => {
              const propertyType = parseInt(Array.from(keys)[0] as string, 10) as PropertyType;
              const nv = {
                ...value,
                propertyType,
              };
              setValue(nv);
              onChange?.(nv);
            }}
          >
            {propertyTypesForManuallySettingValue.map(pt => {
              return (
                <DropdownItem
                  key={pt.type}
                  isReadOnly={!pt.isAvailable}
                  className={pt.isAvailable ? '' : 'text-gray-400 cursor-not-allowed'}
                >
                  <div className={'flex items-center gap-2'}>
                    {t(`PropertyType.${PropertyType[pt.type]}`)}
                    {!pt.isAvailable && (
                      <Tooltip
                        content={pt.unavailableReason}
                        color={'warning'}
                        className={'max-w-[400px]'}
                      >
                        <ExclamationCircleOutlined className={'text-base'} />
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
