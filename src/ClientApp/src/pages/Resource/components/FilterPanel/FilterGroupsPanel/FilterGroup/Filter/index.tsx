'use strict';

import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { DropdownItem, DropdownMenu, DropdownTrigger } from '@nextui-org/react';
import groupStyles from '../index.module.scss';
import type { ResourceSearchFilter } from '../../models';
import PropertySelector from '@/components/PropertySelector';
import ClickableIcon from '@/components/ClickableIcon';
import {
  ResourceProperty,
  PropertyPool,
  SearchOperation, type PropertyType,
} from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';
import { Button, Dropdown, Tooltip } from '@/components/bakaui';
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

  const [filter, setFilter] = useState<ResourceSearchFilter>(propsFilter);

  useUpdateEffect(() => {
    onChange?.(filter);
  }, [filter]);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

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
      setFilter({
        ...filter,
      });
    } else {
      BApi.resource.getFilterValueProperty(filter).then(r => {
        const p = r.data;
        filter.valueProperty = p;
        setFilter({
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
          setFilter({
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
      className={`flex rounded p-1 items-center ${groupStyles.removable}`}
      style={{ backgroundColor: 'var(--bakaui-overlap-background)' }}
    >
      <ClickableIcon
        colorType={'danger'}
        className={groupStyles.remove}
        type={'delete'}
        size={'small'}
        onClick={onRemove}
      />
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
                const availableOperations = (await BApi.resource.getSearchOperationsForProperty({ propertyPool: property.pool, propertyId: property.id })).data || [];
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
