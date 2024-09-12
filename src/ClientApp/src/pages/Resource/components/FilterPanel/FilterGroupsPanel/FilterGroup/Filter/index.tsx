import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import { DropdownItem, DropdownMenu, DropdownTrigger } from '@nextui-org/react';
import groupStyles from '../index.module.scss';
import type { DataPool, IFilter } from '../../models';
import PropertySelector from '@/components/PropertySelector';
import ClickableIcon from '@/components/ClickableIcon';
import {
  ResourceProperty as EnumResourceProperty,
  ResourceProperty,
  ResourcePropertyType,
  SearchOperation,
} from '@/sdk/constants';
import store from '@/store';
import type { IProperty } from '@/components/Property/models';
import { Button, Dropdown, Tooltip } from '@/components/bakaui';
import {
  SearchableReservedPropertySearchOperationsMap,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/models';
import PropertyFilterValueRenderer
  from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/PropertyFilterValueRenderer';
import { buildLogger } from '@/components/utils';

interface IProps {
  filter: IFilter;
  onRemove?: () => any;
  onChange?: (filter: IFilter) => any;
  propertyMap: Record<number, IProperty>;
  dataPool?: DataPool;
}

const log = buildLogger('Filter');

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
  const [property, setProperty] = useState<IProperty>();

  useUpdateEffect(() => {
    onChange?.(filter);
  }, [filter]);

  useEffect(() => {
    if (filter.propertyId) {
       switch (filter!.propertyType!) {
         case ResourcePropertyType.Internal: {
           setProperty(internalOptions.resource.internalResourcePropertyDescriptorMap[filter.propertyId]);
           break;
         }
         case ResourcePropertyType.Reserved: {
           setProperty(internalOptions.resource.reservedResourcePropertyDescriptorMap[filter.propertyId]);
           break;
         }
         case ResourcePropertyType.Custom: {
           setProperty(propertyMap[filter.propertyId]);
           break;
         }
       }
    }
  }, [filter, internalOptions]);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

  log(propsFilter, filter, property);

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
    if (property) {
      operations = property.type == ResourcePropertyType.Custom ? standardValueTypeSearchOperationsMap[property.customPropertyType!] : SearchableReservedPropertySearchOperationsMap[property.id];
    }
    operations ??= [];
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
            {filter.operation == undefined ? t('Filter.Operation') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
          </Button>
        </Tooltip>
      );
    } else {
      if (filter.operation == undefined) {
        filter.operation = operations[0];
      }
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
                      dbValue: undefined,
                      bizValue: undefined,
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
  log('rendering filter', filter, property, propertyMap, dataPool, internalOptions.resource.reservedResourcePropertyDescriptorMap, internalOptions.resource.internalResourcePropertyDescriptorMap);

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
                  type: filter.propertyType!,
                }],
              onSubmit: async (selectedProperties) => {
                const property = selectedProperties[0]!;
                setProperty(property);
                setFilter({
                  ...filter,
                  propertyId: property.id,
                  propertyType: property.type,
                  dbValue: undefined,
                  bizValue: undefined,
                });
              },
              multiple: false,
              pool: ResourcePropertyType.All,
              addable: false,
              editable: false,
            });
          }}
        >
          {(filter.propertyId ? filter.propertyType == ResourcePropertyType.Custom ? propertyMap[filter.propertyId]?.name : t(ResourceProperty[filter.propertyId]) : t('Property')) ?? t('Unknown property')}
        </Button>
      </div>
      <div className={''}>
        {renderOperations()}
      </div>
      {noValue ? null : (filter.operation && property) ? (
        <PropertyFilterValueRenderer
          operation={filter.operation}
          property={property}
          dataPool={dataPool}
          onValueChange={(dbValue, bizValue) => {
            setFilter({
              ...filter,
              dbValue: dbValue,
              bizValue: bizValue,
            });
          }}
          dbValue={filter.dbValue}
          bizValue={filter.bizValue}
        />
      ) : null}
    </div>
  );
};
