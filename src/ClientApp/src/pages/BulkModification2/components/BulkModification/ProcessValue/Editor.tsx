'use strict';

import { useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import { ExclamationCircleOutlined } from '@ant-design/icons';
import type {
  BulkModificationProcessValue,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import type { PropertyPool, StandardValueType } from '@/sdk/constants';
import {
  BulkModificationProcessorValueType,
  bulkModificationProcessorValueTypes,
  PropertyType,
} from '@/sdk/constants';
import {
  Button,
  Checkbox,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Select,
  Spacer,
  Tooltip,
} from '@/components/bakaui';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';
import type { RecursivePartial } from '@/components/types';
import TypeMismatchTip
  from '@/pages/BulkModification2/components/BulkModification/ProcessValue/components/TypeMismatchTip';
import { buildFakeProperty } from '@/pages/BulkModification2/components/BulkModification/ProcessValue/helpers';
import { deserializeStandardValue } from '@/components/StandardValue/helpers';

type Props = {
  onChange?: (value: BulkModificationProcessValue) => any;
  variables?: BulkModificationVariable[];
  value?: BulkModificationProcessValue;
  property?: IProperty;
  baseValueType: PropertyType;
};

const log = buildLogger('ValueWithMultipleTypeEditor');

type PropertyTypeForManuallySettingValue = {
  type: PropertyType;
  isAvailable: boolean;
  unavailableReason?: string;
  properties?: IProperty[];
  bizValueType: StandardValueType;
  dbValueType: StandardValueType;
  isReferenceValueType: boolean;
};

const buildDefaultValue = (property?: IProperty): BulkModificationProcessValue => {
  if (property) {
    return {
      type: BulkModificationProcessorValueType.ManuallyInput,
      editorPropertyType: property.type,
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
    property,
    baseValueType,
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

  log(props, value);

  const onFollowPropertyChanges = async (follow: boolean) => {
    if (value.value) {
      if (!value.followPropertyChanges && follow) {
        value.value = deserializeStandardValue((await BApi.property.getPropertyDbValue(value.value))?.data, value);
      }
    }
    if (isSelected) {
      BApi.property.getAvailablePropertyTypesForManuallySettingValue();
    }
    changeValue({
      ...value,
      followPropertyChanges: isSelected,
    });
  };

  const changeValue = (patches: Partial<BulkModificationProcessValue>) => {
    const nv = {
      ...value,
      ...patches,
    };
    setValue(nv);
    onChange?.(nv);
  };

  const renderEditor = () => {
    if (!value.editorPropertyType) {
      return null;
    }

    const pv = propertyTypesForManuallySettingValue.find(pt => pt.type == value.editorPropertyType);
    if (!pv || !pv.isAvailable) {
      return null;
    }

    let property = propertyTypesForManuallySettingValue.find(pt => pt.type == value.editorPropertyType)?.properties?.find(p => p.id == value.propertyId && p.pool == value.propertyPool);
    return (
      <>
        {(!pv.isReferenceValueType || property) && (
          <>
            <Spacer x={2} />
            <PropertyValueRenderer
              property={property ?? buildFakeProperty(pv.type, pv.dbValueType, pv.bizValueType)}
              onValueChange={(dv, bv) => {
                changeValue({
                  value: value.followPropertyChanges ? dv : bv,
                });
              }}
              dbValue={value.followPropertyChanges ? value.value : undefined}
              bizValue={value.followPropertyChanges ? undefined : value.value}
            />
          </>
        )}
        {pv.isReferenceValueType && property && (
          <Tooltip
            content={(
              <div className={'flex flex-col gap-1 max-w-[400px]'}>
                {['By enabling this options data will be changed according to the changes in dynamic properties (single choice, multiple choice, multilevel data, etc.).',
                  'For example, if you use the \'actor\' from the multiple-choice data as the change result, then in the future, if you modify \'actor\' to \'actor1\', the current data will also be changed to \'actor1\' after next time processing. However, if this options is not enabled, the current data will be changed to \'actor\' after next time processing.',
                  'For properties(such as text, numbers, dates, etc.) without reference values, there is no difference between statuses of this options.'].map(p => {
                  return (
                    <p>{t(p)}</p>
                  );
                })}
              </div>
            )}
            color={'secondary'}
          >
            <Checkbox
              checked={value.followPropertyChanges}
              onValueChange={isSelected => {

              }}
            >
              {t('Follow property changes')}
            </Checkbox>
          </Tooltip>
        )}
      </>
    );
  };

  const renderPropertyCandidates = () => {
    if (value.editorPropertyType) {
      const pts = propertyTypesForManuallySettingValue.find(pt => pt.type == value.editorPropertyType);
      if (pts?.properties && pts.properties.length > 0) {
        const property = pts.properties.find(p => p.id == value.propertyId && p.pool == value.propertyPool);
        return (
          <Dropdown>
            <DropdownTrigger>
              <Button
                // size={'sm'}
                variant="bordered"
              >
                {property ? `(${property.poolName})${property.name}` : t('Choose a property')}
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              selectionMode={'single'}
              aria-label="Static Actions"
              onSelectionChange={keys => {
                const key = Array.from(keys)[0] as string;
                const segments = key.split('-');
                const pool = parseInt(segments[0], 10) as PropertyPool;
                const id = parseInt(segments[1], 10);
                changeValue({
                  propertyPool: pool,
                  propertyId: id,
                });
              }}
            >
              {pts.properties.map(s => {
                return (
                  <DropdownItem key={`${s.pool}-${s.id}`}>
                    <div className={'flex items-center gap-2'}>
                      {`(${s.poolName})${s.name}`}
                    </div>
                  </DropdownItem>
                );
              })}
            </DropdownMenu>
          </Dropdown>
        );
      }
    }
    return null;
  };

  const renderByValueType = (valueType?: BulkModificationProcessorValueType) => {
    if (!valueType) {
      return null;
    }
    switch (valueType) {
      case BulkModificationProcessorValueType.ManuallyInput:
        return (
          <>
            <Dropdown>
              <DropdownTrigger>
                <Button
                  // size={'sm'}
                  variant="bordered"
                >
                  {value.editorPropertyType ? (
                    `${t('Use {{any}}', { any: `${t(PropertyType[value.editorPropertyType])} ${t('editor')}` })}`
                  ) : t('Choose a property value editor')}
                </Button>
              </DropdownTrigger>
              <DropdownMenu
                className={'max-h-[40vh] overflow-y-auto'}
                selectionMode={'single'}
                aria-label="Static Actions"
                onSelectionChange={keys => {
                  const editorPropertyType = parseInt(Array.from(keys)[0] as string, 10) as PropertyType;
                  changeValue({ editorPropertyType });
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
            {renderPropertyCandidates()}
            {renderEditor()}
          </>
        );
      case BulkModificationProcessorValueType.Variable:
        if (!variables || variables.length == 0) {
          return t('No variables available');
        }
        return (
          <Select
            placeholder={t('Please select a variable')}
            dataSource={variables.map(v => ({
              label: v.name,
              value: v.key,
            }))}
            selectionMode={'single'}
            isRequired
            onSelectionChange={keys => {
              const key = Array.from(keys)[0] as string;
              changeValue({
                value: key,
              });
            }}
          />
        );
    }
  };

  const renderTypeMismatchTip = () => {
    let selectedType: PropertyType | undefined;
    switch (value.type) {
      case BulkModificationProcessorValueType.ManuallyInput:
        selectedType = value.editorPropertyType;
        break;
      case BulkModificationProcessorValueType.Variable:
        selectedType = variables?.find(v => v.key == value.value)?.property.type;
        break;
    }

    if (!selectedType) {
      return null;
    }

    return (
      <TypeMismatchTip fromType={selectedType} toType={baseValueType} />
    );
  };

  return (
    <div className={'flex items-center gap-1'}>
      <Dropdown>
        <DropdownTrigger>
          <Button
            // size={'sm'}
            variant="bordered"
          >
            {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[value.type]}`)}
          </Button>
        </DropdownTrigger>
        <DropdownMenu
          variant={'flat'}
          selectionMode={'single'}
          aria-label="Static Actions"
          onSelectionChange={keys => {
            const type = parseInt(Array.from(keys)[0] as string, 10) as BulkModificationProcessorValueType;
            changeValue({ type });
          }}
        >
          {bulkModificationProcessorValueTypes.map(s => {
            return (
              <DropdownItem key={s.value}>
                <div className={'flex items-center gap-2'}>
                  {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[s.value]}`)}
                </div>
              </DropdownItem>
            );
          })}
        </DropdownMenu>
      </Dropdown>
      {renderByValueType(value.type)}
      {renderTypeMismatchTip()}
    </div>
  );
};
