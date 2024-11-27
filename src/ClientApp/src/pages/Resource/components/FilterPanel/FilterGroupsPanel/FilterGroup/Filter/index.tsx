'use strict';

import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { DropdownItem, DropdownMenu, DropdownTrigger } from '@nextui-org/react';
import { ApiOutlined, DeleteOutlined, DisconnectOutlined } from '@ant-design/icons';
import type { ResourceSearchFilter } from '../../models';
import PropertySelector from '@/components/PropertySelector';
import { PropertyPool, SearchOperation } from '@/sdk/constants';
import { Button, Dropdown, Tooltip } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import DeleteAndDisable from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/DeleteAndDisable';

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
              return (
                <DropdownItem
                  key={operation}
                  onClick={() => {
                    refreshValue({
                      ...filter,
                      operation: operation,
                    });
                  }}
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

    return (
      <PropertyValueRenderer
        property={filter.valueProperty}
        variant={'light'}
        bizValue={filter.bizValue}
        dbValue={filter.dbValue}
        onValueChange={(dbValue, bizValue) => {
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
    <div
      className={`flex rounded p-1 items-center ${filter.disabled ? '' : 'group/filter-operations'} relative rounded`}
      style={{ backgroundColor: 'var(--bakaui-overlap-background)' }}
    >
      {filter.disabled && (
        <div
          className={'absolute top-0 left-0 w-full h-full flex items-center justify-center z-20 group/filter-disable-cover rounded cursor-not-allowed'}
          style={{ backgroundColor: 'hsla(from var(--bakaui-color) h s l / 50%)' }}
        >
          <Tooltip
            content={t('Click to enable')}
          >
            <Button
              size={'sm'}
              variant={'light'}
              isIconOnly
              onClick={() => {
                changeFilter({
                  ...filter,
                  disabled: false,
                });
              }}
            >
              <ApiOutlined className={'text-base group-hover/filter-disable-cover:block text-success hidden'} />
              <DisconnectOutlined className={'text-base group-hover/filter-disable-cover:hidden block'} />
            </Button>
          </Tooltip>
        </div>
      )}
      <div
        className={'group-hover/filter-operations:flex hidden absolute top-[-10px] right-[-10px] z-10'}
      >
        <Button
          size={'sm'}
          variant={'light'}
          color={'warning'}
          isIconOnly
          className={'w-auto min-w-fit px-1'}
          onClick={() => {
            changeFilter({
              ...filter,
              disabled: true,
            });
          }}
        >
          <DisconnectOutlined className={'text-base'} />
        </Button>
        <Button
          size={'sm'}
          variant={'light'}
          color={'danger'}
          isIconOnly
          className={'w-auto min-w-fit px-1'}
          onClick={onRemove}
        >
          <DeleteOutlined className={'text-base'} />
        </Button>
      </div>
      <div className={''}>
        <Button
          className={'min-w-fit pl-2 pr-2'}
          color={'primary'}
          variant={'light'}
          size={'sm'}
          onClick={() => {
            PropertySelector.show({
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
  );
};
