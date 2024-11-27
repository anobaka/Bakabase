'use strict';

import { useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
import { Button, Dropdown, DropdownItem, DropdownMenu, DropdownTrigger, Select } from '@/components/bakaui';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import type { IProperty } from '@/components/Property/models';

type Props = {
  Component: React.FC<{ onChange: (bv: string, dv: string) => any }>;
  // children?: any;
  onChange?: (valueType: BulkModificationProcessorValueType, value?: string) => any;
  variables?: BulkModificationVariable[];
  valueTypes?: BulkModificationProcessorValueType[];
  property: IProperty;
  value?: string;
};

export default ({
                  // children,
  Component,
                  onChange,
                  valueTypes = [BulkModificationProcessorValueType.BizValue],
                  variables,
                  property,
                  value: propsValue,
                }: Props) => {
  const { t } = useTranslation();
  const [valueType, setValueType] = useState<BulkModificationProcessorValueType>(BulkModificationProcessorValueType.BizValue);
  const [value, setValue] = useState<string | undefined>(propsValue);

  const renderEditor = () => {
    switch (valueType) {
      case BulkModificationProcessorValueType.BizValue:
        // return children;
        // return (
        //   <Component onChange={(bv, dv) => {
        //     setValue(bv);
        //     onChange?.(valueType, bv);
        //   }
        //   />
        // );
      case BulkModificationProcessorValueType.DbValue:
        return (
          <PropertyValueRenderer
            property={property}
            dbValue={value}
            variant={'light'}
            onValueChange={(dbValue, bizValue) => { onChange?.(valueType, dbValue); }}
          />
        );
        // return children;
        // return (
        //   <Component onChange={(bv, dv) => {
        //     setValue(dv);
        //     onChange?.(valueType, dv);
        //   }}
        //   />
        // );
      case BulkModificationProcessorValueType.VariableKey:
        return (
          <Select
            dataSource={variables?.map(v => ({
              label: v.name,
              value: v.key,
            }))}
            value={value}
            onSelectionChange={keys => {
              const key = Array.from(keys)[0] as string;
              setValue(key);
              onChange?.(valueType, key);
            }}
            multiple={false}
          />
        );
    }
  };

  return (
    <div>
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
              const vt = Array.from(keys)[0] as number;
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
