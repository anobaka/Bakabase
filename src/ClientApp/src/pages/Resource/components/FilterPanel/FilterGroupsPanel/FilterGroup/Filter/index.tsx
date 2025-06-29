'use strict';

import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { ApiOutlined, DeleteOutlined, DisconnectOutlined } from '@ant-design/icons';
import { AiOutlineClose } from 'react-icons/ai';
import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from 'react-icons/md';
import type { ResourceSearchFilter } from '../../models';
import PropertySelector from '@/components/PropertySelector';
import { PropertyPool, PropertyType, SearchOperation } from '@/sdk/constants';
import { Button, Dropdown, Tooltip, DropdownItem, DropdownMenu, DropdownTrigger } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';

interface IProps {
  filter: ResourceSearchFilter;
  onRemove?: () => any;
  onChange?: (filter: ResourceSearchFilter) => any;
}

const log = buildLogger('Filter');

export default ({
                  filter: propsFilter,
                  onRemove,
                  onChange,
                }: IProps) => {
  const { t } = useTranslation();

  const a = t('a');

  const [filter, setFilter] = useState<ResourceSearchFilter>(propsFilter);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

  const changeFilter = (newFilter: ResourceSearchFilter) => {
    setFilter(newFilter);
    onChange?.(newFilter);
  };

  log(propsFilter, filter);

  const renderOperations = () => {
    if (filter.propertyId == undefined) {
      return (
        <Tooltip
          content={t('Please select a property first')}
        >
          <Button
            className={'min-w-fit pl-2 pr-2 cursor-not-allowed'}
            variant={'light'}
            color={'secondary'}
            size={'sm'}
          >
            {filter.operation == undefined ? t('Condition') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
          </Button>
        </Tooltip>
      );
    }

    const operations = filter.availableOperations ?? [];
    log(operations);
    if (operations.length == 0) {
      return (
        <Tooltip
          content={t('Can not operate on this property')}
        >
          <Button
            className={'min-w-fit pl-2 pr-2 cursor-not-allowed'}
            variant={'light'}
            color={'secondary'}
            size={'sm'}
          >
            {filter.operation == undefined ? t('Condition') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
          </Button>
        </Tooltip>
      );
    } else {
      return (
        <Dropdown placement={'bottom-start'}>
          <DropdownTrigger>
            <Button
              className={'min-w-fit pl-2 pr-2'}
              variant={'light'}
              color={'secondary'}
              size={'sm'}
            >
              {filter.operation == undefined ? t('Condition') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
            </Button>
          </DropdownTrigger>
          <DropdownMenu>
            {operations.map((operation) => {
              const descriptionKey = `SearchOperation.${SearchOperation[operation]}.description`;
              const description = t(descriptionKey);
              return (
                <DropdownItem
                  key={operation}
                  onClick={() => {
                      refreshValue({
                        ...filter,
                        operation: operation,
                      });
                    }}
                  description={description == descriptionKey ? undefined : description}
                >
                  {t(`SearchOperation.${SearchOperation[operation]}`)}
                </DropdownItem>
              );
            })}
          </DropdownMenu>
        </Dropdown>
      );
    }
  };

  log('rendering filter', filter);

  const refreshValue = (filter: ResourceSearchFilter) => {
    if (!filter.propertyPool || !filter.propertyId || !filter.operation) {
      filter.valueProperty = undefined;
      filter.dbValue = undefined;
      filter.bizValue = undefined;
      changeFilter({
        ...filter,
      });
    } else {
      BApi.resource.getFilterValueProperty(filter).then(r => {
        const p = r.data;
        filter.valueProperty = p;
        changeFilter({
          ...filter,
        });
      });
    }
  };

  const renderValue = () => {
    if (!filter.valueProperty) {
      return null;
    }

    if (filter.operation == SearchOperation.IsNull || filter.operation == SearchOperation.IsNotNull) {
      return null;
    }

    return (
      <PropertyValueRenderer
        property={filter.valueProperty}
        variant={'light'}
        bizValue={filter.bizValue}
        dbValue={filter.dbValue}
        onValueChange={(dbValue, bizValue) => {
          console.log('123', dbValue, bizValue);
          changeFilter({
            ...filter,
            dbValue: dbValue,
            bizValue: bizValue,
          });
        }}
        defaultEditing={filter.dbValue == undefined}
      />
    );
  };

  return (
    <Tooltip
      showArrow
      offset={0}
      placement={'top'}
      content={(
        <>
          <div className={'flex items-center'}>
            <Button
              size={'sm'}
              variant={'light'}
              color={filter.disabled ? 'success' : 'warning'}
              isIconOnly
              // className={'w-auto min-w-fit px-1'}
              onPress={() => {
                changeFilter({
                  ...filter,
                  disabled: !filter.disabled,
                });
              }}
            >
              {filter.disabled ? (
                <MdOutlineFilterAlt className={'text-lg'} />
              ) : (
                <MdOutlineFilterAltOff className={'text-lg'} />
              )}
            </Button>
            <Button
              size={'sm'}
              variant={'light'}
              color={'danger'}
              isIconOnly
              // className={'w-auto min-w-fit px-1'}
              onPress={onRemove}
            >
              <AiOutlineClose className={'text-base'} />
            </Button>
          </div>
        </>
      )}
    >
      <div
        className={`flex rounded p-1 items-center ${filter.disabled ? '' : 'group/filter-operations'} relative rounded`}
        style={{ backgroundColor: 'var(--bakaui-overlap-background)' }}
      >
        {filter.disabled && (
          <div
            className={'absolute top-0 left-0 w-full h-full flex items-center justify-center z-20 group/filter-disable-cover rounded cursor-not-allowed'}
          >
            <MdOutlineFilterAltOff className={'text-lg text-warning'} />
          </div>
        )}
        <div
          className={`flex items-center ${filter.disabled ? 'opacity-60' : ''}`}
        >
          <div className={''}>
            <Button
              className={'min-w-fit pl-2 pr-2'}
              color={'primary'}
              variant={'light'}
              size={'sm'}
              onPress={() => {
                PropertySelector.show({
                  v2: true,
                  selection: filter.propertyId == undefined
                    ? undefined
                    : [{
                      id: filter.propertyId,
                      pool: filter.propertyPool!,
                    }],
                  onSubmit: async (selectedProperties) => {
                    const property = selectedProperties[0]!;
                    const availableOperations = (await BApi.resource.getSearchOperationsForProperty({
                      propertyPool: property.pool,
                      propertyId: property.id,
                    })).data || [];
                    const nf = {
                      ...filter,
                      propertyId: property.id,
                      propertyPool: property.pool,
                      dbValue: undefined,
                      bizValue: undefined,
                      property,
                      availableOperations,
                    };
                    refreshValue(nf);
                  },
                  multiple: false,
                  pool: PropertyPool.All,
                  addable: false,
                  editable: false,
                });
              }}
            >
              {filter.property ? filter.property.name ?? t('Unknown property') : t('Property')}
            </Button>
          </div>
          <div className={''}>
            {renderOperations()}
          </div>
          {renderValue()}
        </div>
      </div>
    </Tooltip>
  );
};
