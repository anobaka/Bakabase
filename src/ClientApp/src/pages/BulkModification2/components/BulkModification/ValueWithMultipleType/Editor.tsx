'use strict';

import { useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
import { Button, Dropdown, DropdownItem, DropdownMenu, DropdownTrigger, Select } from '@/components/bakaui';
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
            variant={'light'}
            onValueChange={(dbValue, bizValue) => { onChange?.(valueType, bizValue); }}
          />
        );
      case BulkModificationProcessorValueType.Dynamic:
        return (
          <PropertyValueRenderer
            property={property}
            dbValue={value}
            variant={'light'}
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
              {t(BulkModificationProcessorValueType[valueType])}
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
              return (
                <DropdownItem key={v.toString()}>{t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[v]}`)}</DropdownItem>
              );
            })}
          </DropdownMenu>
        </Dropdown>
      )}
      {renderEditor()}
    </div>
  );
};
