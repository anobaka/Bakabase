import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { DropdownItem, DropdownMenu, DropdownTrigger } from '@nextui-org/react';
import groupStyles from '../index.module.scss';
import type { DataPool, IFilter } from '../../models';
import FilterValue from './FilterValue';
import PropertySelector from '@/components/PropertySelector';
import ClickableIcon from '@/components/ClickableIcon';
import { ResourceProperty, SearchOperation } from '@/sdk/constants';
import store from '@/store';
import type { IProperty } from '@/components/Property/models';
import { Button, Dropdown, Tooltip } from '@/components/bakaui';
import {
  SearchableReservedPropertySearchOperationsMap,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/models';

interface IProps {
  filter: IFilter;
  onRemove?: () => any;
  onChange?: (filter: IFilter) => any;
  propertyMap: Record<number, IProperty>;
  dataPool?: DataPool;
}

export default ({
                  filter: propsFilter,
                  onRemove,
                  onChange,
                  propertyMap,
                  dataPool,
                }: IProps) => {
  const { t } = useTranslation();

  const internalOptions = store.useModelState('internalOptions');
  const standardValueTypeSearchOperationsMap = internalOptions?.resource?.customPropertyValueSearchOperationsMap || {};

  const [filter, setFilter] = useState<IFilter>(propsFilter);

  useUpdateEffect(() => {
    onChange?.(filter);
  }, [filter]);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

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
            {filter.operation == undefined ? t('Filter.Operation') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
          </Button>
        </Tooltip>
      );
    }

    let operations: SearchOperation[] | undefined;
    const property = propertyMap?.[filter.propertyId!];
    if (property) {
      operations = property.isReserved ? SearchableReservedPropertySearchOperationsMap[property.id] : standardValueTypeSearchOperationsMap[property.valueType];
    }
    operations ??= [];
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
            {filter.operation == undefined ? t('Filter.Operation') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
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
              {filter.operation == undefined ? t('Filter.Operation') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
            </Button>
          </DropdownTrigger>
          <DropdownMenu>
            {operations.map((operation) => {
              return (
                <DropdownItem
                  key={operation}
                  onClick={() => {
                    setFilter({
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

  const noValue = filter.operation == SearchOperation.IsNull || filter.operation == SearchOperation.IsNotNull;
  const property = propertyMap[filter.propertyId!];

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
                : [{ id: filter.propertyId, isReserved: filter.isReservedProperty! }],
              onSubmit: async (selectedProperties) => {
                const property = selectedProperties[0]!;
                setFilter({
                  ...filter,
                  propertyId: property.id,
                  isReservedProperty: property.isReserved,
                });
              },
              multiple: false,
              pool: 'all',
              addable: false,
              editable: false,
            });
          }}
        >
          {(filter.propertyId ? filter.isReservedProperty ? t(ResourceProperty[filter.propertyId]) : propertyMap[filter.propertyId]?.name : t('Property')) ?? t('Unknown property')}
        </Button>
      </div>
      <div className={''}>
        {renderOperations()}
      </div>
      {noValue ? null : (filter.operation && property) ? (
        <FilterValue
          dataPool={dataPool}
          value={filter.value}
          property={property}
          onChange={value => {
            setFilter({
              ...filter,
              value,
            });
          }}
        />
      ) : null}
    </div>
  );
};
