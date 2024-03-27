import { Button, Dropdown, Menu } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { IFilter } from '../../../../models';
import groupStyles from '../../index.module.scss';
import styles from './index.module.scss';
import FilterValue from './components/FilterValue';
import PropertySelector from '@/components/PropertySelector';
import ClickableIcon from '@/components/ClickableIcon';
import type { StandardValueType } from '@/sdk/constants';
import { SearchOperation } from '@/sdk/constants';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import store from '@/store';

interface IProps {
  filter: IFilter;
  onRemove?: () => any;
  onChange?: (filter: IFilter) => any;
}

export default ({
                  filter: propsFilter,
                  onRemove,
                  onChange,
                }: IProps) => {
  const { t } = useTranslation();

  const reservedOptions = store.useModelState('reservedOptions');
  const standardValueTypeSearchOperationsMap = reservedOptions?.resource?.standardValueSearchOperationsMap || {};

  const [filter, setFilter] = useState<IFilter>(propsFilter);

  useUpdateEffect(() => {
    console.log(123);
    onChange?.(filter);
  }, [filter]);

  const renderOperations = () => {
    if (filter.propertyId == undefined) {
      return (
        <Menu>
          <Menu.Item disabled>{t('Please select a property first')}</Menu.Item>
        </Menu>
      );
    }
    const operations = standardValueTypeSearchOperationsMap[filter.valueType!] || [];
    if (operations.length == 0) {
      return (
        <Menu>
          <Menu.Item disabled>{t('Can not operate on this property')}</Menu.Item>
        </Menu>
      );
    } else {
      return (
        <Menu>
          {operations.map((operation) => {
            return (
              <Menu.Item
                key={operation}
                onClick={() => {
                  setFilter({
                    ...filter,
                    operation: operation,
                  });
                }}
              >
                {t(`SearchOperation.${SearchOperation[operation]}`)}
              </Menu.Item>
            );
          })}
        </Menu>
      );
    }
  };

  const noValue = filter.operation == SearchOperation.IsNull || filter.operation == SearchOperation.IsNotNull;

  return (
    <div className={`${styles.filter} ${groupStyles.removable}`}>
      <ClickableIcon
        colorType={'danger'}
        className={groupStyles.remove}
        type={'delete'}
        size={'small'}
        onClick={onRemove}
      />
      <div className={styles.property}>
        <Button
          type={'primary'}
          text
          onClick={() => {
            PropertySelector.show({
              selection: { [filter.isReservedProperty ? 'reservedPropertyIds' : 'customPropertyIds']: filter.propertyId == undefined ? undefined : [filter.propertyId] },
              onSubmit: async (selectedProperties) => {
                const property = (selectedProperties.reservedProperties?.[0] ?? selectedProperties.customProperties?.[0])!;
                const cp = property as ICustomProperty;
                setFilter({
                  ...filter,
                  propertyId: property.id,
                  propertyName: property.name,
                  isReservedProperty: cp == undefined,
                  valueType: property.type as unknown as StandardValueType,
                });
              },
              multiple: false,
              pool: 'all',
            });
          }}
          size={'small'}
        >
          {filter.propertyId ? filter.propertyName : t('Property')}
        </Button>
      </div>
      <div className={styles.operation}>
        <Dropdown
          trigger={(
            <Button
              type={'primary'}
              text
              size={'small'}
              disabled={filter.propertyId == undefined}
            >
              {filter.operation == undefined ? t('Operation') : t(`SearchOperation.${SearchOperation[filter.operation]}`)}
            </Button>
          )}
          triggerType={'click'}
        >
          {renderOperations()}
        </Dropdown>
      </div>
      {noValue ? null : (
        <FilterValue
          operation={filter.operation}
          valueType={filter.valueType}
          value={filter.value}
          onChange={value => {
            setFilter({
              ...filter,
              value,
            });
          }}
        />
      )}
    </div>
  );
};
