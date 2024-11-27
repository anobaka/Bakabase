'use strict';

import { useTranslation } from 'react-i18next';
import React, { useState } from 'react';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
import { Button, Dropdown, DropdownItem, DropdownMenu, DropdownTrigger, Select } from '@/components/bakaui';

type Props = {
  Component: React.FC<{ onChange: (bv: string, dv: string) => any }>;
  onChange?: (valueType: BulkModificationProcessorValueType, value?: string) => any;
  variables?: BulkModificationVariable[];
  valueTypes?: BulkModificationProcessorValueType[];
};

export default ({
                  Component,
                  onChange,
                  valueTypes = [BulkModificationProcessorValueType.BizValue],
                  variables,
                }: Props) => {
  const { t } = useTranslation();
  const [valueType, setValueType] = useState<BulkModificationProcessorValueType>(BulkModificationProcessorValueType.BizValue);
  const [value, setValue] = useState<string>();

  const renderEditor = () => {
    switch (valueType) {
      case BulkModificationProcessorValueType.BizValue:
        return (
          <Component onChange={(bv, dv) => {
            setValue(bv);
          }}
          />
        );
      case BulkModificationProcessorValueType.DbValue:
        return (
          <Component onChange={(bv, dv) => {
            setValue(dv);
          }}
          />
        );
      case BulkModificationProcessorValueType.VariableKey:
        return (
          <Select
            dataSource={variables?.map(v => ({
              label: v.name,
              value: v.key,
            }))}
            value={value}
            onSelectionChange={keys => {
              setValue(Array.from(keys)[0] as string);
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
