'use strict';

import { setDefaults, useTranslation } from 'react-i18next';
import React, { useCallback, useEffect, useRef, useState } from 'react';
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
import TypeMismatchTip
  from '@/pages/BulkModification2/components/BulkModification/ProcessValue/components/TypeMismatchTip';
import { deserializeStandardValue } from '@/components/StandardValue/helpers';

type Props = {
  onChange?: (value: BulkModificationProcessValue) => any;
  variables?: BulkModificationVariable[];
  value?: BulkModificationProcessValue;
  baseValueType: PropertyType;
  preferredProperty?: IProperty;
  availableValueTypes?: BulkModificationProcessorValueType[];
};

type PropertyTypeForManuallySettingValue = {
  type: PropertyType;
  isAvailable: boolean;
  unavailableReason?: string;
  properties?: IProperty[];
  bizValueType: StandardValueType;
  dbValueType: StandardValueType;
  isReferenceValueType: boolean;
};

const log = buildLogger('BulkModificationProcessValueEditor');

export default (props: Props) => {
  const {
    onChange,
    variables,
    value: propsValue,
    baseValueType,
    preferredProperty,
    availableValueTypes,
  } = props;
  const { t } = useTranslation();

  const [propertyTypesForManuallySettingValue, setPropertyTypesForManuallySettingValue] = useState<PropertyTypeForManuallySettingValue[]>([]);
  const [value, setValue] = useState<BulkModificationProcessValue>(propsValue ?? { type: availableValueTypes?.[0] ?? BulkModificationProcessorValueType.ManuallyInput });
  const valueRef = useRef(value);
  const [error, setError] = useState<string>();

  useEffect(() => {
    valueRef.current = value;
  }, [value]);

  log(value, preferredProperty, propertyTypesForManuallySettingValue);

  useEffect(() => {
    BApi.property.getAvailablePropertyTypesForManuallySettingValue().then(r => {
      const pvs = r.data || [];
      setPropertyTypesForManuallySettingValue(pvs);
    });
  }, []);

  const changeValue = useCallback((patches: Partial<BulkModificationProcessValue>, triggerOnChange: boolean = true) => {
    const nv = {
      ...valueRef.current,
      ...patches,
    };

    switch (nv.type) {
      case BulkModificationProcessorValueType.ManuallyInput: {
        nv.editorPropertyType ??= preferredProperty?.type ?? propertyTypesForManuallySettingValue?.[0]?.type ?? baseValueType;

        if (!nv.property) {
          const pv = propertyTypesForManuallySettingValue.find(pt => pt.type == nv.editorPropertyType);
          const property = pv?.properties?.find(p => p.id == nv.propertyId && p.pool == nv.propertyPool) ?? preferredProperty?.type == nv.editorPropertyType ? preferredProperty : pv?.properties?.[0];
          if (property) {
            nv.property = property;
            nv.propertyId = property.id;
            nv.propertyPool = property.pool;
          }
        }
        break;
      }
      case BulkModificationProcessorValueType.Variable: {
        // if (!nv.value && variables && variables.length > 0) {
        //   nv.value = variables[0].key;
        // }
        break;
      }
    }

    setValue(nv);

    if (triggerOnChange) {
      onChange?.(nv);
    }
  }, [preferredProperty, propertyTypesForManuallySettingValue, baseValueType, onChange]);

  useEffect(() => {
    changeValue(valueRef.current, false);
  }, [changeValue, propertyTypesForManuallySettingValue]);

  const onFollowPropertyChanges = async (follow: boolean) => {
    const { property } = value;
    if (!property) {
      return;
    }

    if (value.value) {
      if (!value.followPropertyChanges && follow) {
        value.value = deserializeStandardValue((await BApi.property.getPropertyDbValue(property.pool, property.id, { bizValue: value.value }))?.data ?? null, property.dbValueType);
      } else {
        if (value.followPropertyChanges && !follow) {
          value.value = deserializeStandardValue((await BApi.property.getPropertyBizValue(property.pool, property.id, { dbValue: value.value }))?.data ?? null, property.bizValueType);
        }
      }
    }

    value.followPropertyChanges = follow;
    changeValue({ ...value });
  };


  const renderEditor = () => {
    if (!value.editorPropertyType) {
      return null;
    }

    const pv = propertyTypesForManuallySettingValue.find(pt => pt.type == value.editorPropertyType);
    if (!pv || !pv.isAvailable) {
      return null;
    }

    return (
      <>
        {(value.property) && (
          <>
            <Spacer x={2} />
            <PropertyValueRenderer
              property={value.property}
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
        {pv.isReferenceValueType && value.property && (
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
              defaultSelected
              isSelected={value.followPropertyChanges}
              onValueChange={onFollowPropertyChanges}
              className={'ml-2'}
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
      if (pts?.properties && pts.isReferenceValueType) {
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
                  property,
                  value: undefined,
                  followPropertyChanges: pts?.isReferenceValueType,
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

                  changeValue({
                    editorPropertyType,
                    property: undefined,
                    propertyId: undefined,
                    propertyPool: undefined,
                    value: undefined,
                    followPropertyChanges: undefined,
                  });
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
            selectedKeys={value.value ? [value.value] : undefined}
            onSelectionChange={keys => {
              const key = Array.from(keys)[0] as string;
              changeValue({
                value: key,
                editorPropertyType: undefined,
                property: undefined,
                propertyId: undefined,
                propertyPool: undefined,
                followPropertyChanges: undefined,
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

  const valueTypes: BulkModificationProcessorValueType[] = availableValueTypes ?? bulkModificationProcessorValueTypes.map(x => x.value);

  return (
    <div>
      <div className={'flex items-center gap-1'}>
        <Dropdown isDisabled={valueTypes.length == 1}>
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
            {valueTypes.map(s => {
              return (
                <DropdownItem key={s}>
                  <div className={'flex items-center gap-2'}>
                    {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[s]}`)}
                  </div>
                </DropdownItem>
              );
            })}
          </DropdownMenu>
        </Dropdown>
        {renderByValueType(value.type)}
        {renderTypeMismatchTip()}
      </div>
      {error && (
        <div className={'text-danger'}>
          {t(error)}
        </div>
      )}
    </div>
  );
};
