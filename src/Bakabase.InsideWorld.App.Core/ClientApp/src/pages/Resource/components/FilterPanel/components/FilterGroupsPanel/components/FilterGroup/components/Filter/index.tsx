import { Button, Dropdown, Menu, Search } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import type { IFilter } from '../../../../models';
import groupStyles from '../../index.module.scss';
import styles from './index.module.scss';
import FilterValue from './components/FilterValue';
import PropertySelector from '@/components/PropertySelector';
import ClickableIcon from '@/components/ClickableIcon';
import type { StandardValueType } from '@/sdk/constants';
import { SearchOperation, searchOperations } from '@/sdk/constants';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import store from '@/store';
interface IProps {
  filter: IFilter;
  onRemove?: () => any;
}

export default ({ filter: propsFilter, onRemove }: IProps) => {
  const { t } = useTranslation();

  const reservedOptions = store.useModelState('reservedOptions');
  const standardValueTypeSearchOperationsMap = reservedOptions?.resource?.standardValueSearchOperationsMap || {};

  const [filter, setFilter] = useState<IFilter>(propsFilter);

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
                {t(SearchOperation[operation])}
              </Menu.Item>
            );
          })}
        </Menu>
      );
    }
  };

  return (
    <div className={`${styles.filter} ${groupStyles.removable}`} >
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
            >
              {filter.operation == undefined ? t('Operation') : t(SearchOperation[filter.operation])}
            </Button>
          )}
        >
          {renderOperations()}
        </Dropdown>
      </div>
      <FilterValue filter={filter} />
    </div>
  );
};
