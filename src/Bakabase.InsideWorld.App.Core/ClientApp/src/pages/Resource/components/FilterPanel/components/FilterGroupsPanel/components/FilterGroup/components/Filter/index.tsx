import { Button, Dropdown, Menu, Search } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import type { IFilter } from '../../../../models';
import groupStyles from '../../index.module.scss';
import styles from './index.module.scss';
import PropertySelector from '@/components/PropertySelector';
import ClickableIcon from '@/components/ClickableIcon';
import { SearchOperation, searchOperations } from '@/sdk/constants';
interface IProps {
  filter: IFilter;
  onRemove?: () => any;
}

export default ({ filter: propsFilter, onRemove }: IProps) => {
  const { t } = useTranslation();

  const [filter, setFilter] = useState<IFilter>(propsFilter);
  return (
    <div className={`${styles.filter} ${groupStyles.removable}`} >
      <ClickableIcon
        colorType={'danger'}
        className={groupStyles.remove}
        type={'delete'}
        size={'small'}
        onClick={onRemove}
      />
      <div className="property">
        <Button
          type={'primary'}
          text
          onClick={() => {
            PropertySelector.show({
              selectedKeys: filter.propertyId ? [{ id: filter.propertyId, isReserved: filter.isReservedProperty! }] : undefined,
              onSubmit: async (reservedProperties, customProperties) => {
                const property = reservedProperties?.[0] ?? customProperties?.[0];
                setFilter({
                  ...filter,
                  propertyId: property.id,
                  propertyName: property.name,
                  isReservedProperty: reservedProperties[0].isReserved,
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
      <div className="operation">
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
          <Menu>
            {searchOperations.map((operation) => {
              return (
                <Menu.Item
                  key={operation.value}
                  onClick={() => {
                    setFilter({
                      ...filter,
                      operation: operation.value,
                    });
                  }}
                >
                  {t(operation.label)}
                </Menu.Item>
              );
            })}
          </Menu>
        </Dropdown>
      </div>
    </div>
  );
};
