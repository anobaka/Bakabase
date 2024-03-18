import { Button, Dropdown, Menu } from '@alifd/next';
import React from 'react';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';
import { useTranslation } from 'react-i18next';

export default () => {
  const { t } = useTranslation();
  return (
    <div className="group" id={'resource-page-filter-groups'}>
      <Dropdown
        trigger={(
          <Button
            type={'normal'}
            className={'add-filter-or-group'}
            size={'small'}
          >
            <CustomIcon
              type={'add-filter'}
              size={'small'}
            />
          </Button>
        )}
        align={'tl tr'}
        triggerType={'click'}
      >
        <Menu className={'add-filter-or-group-dropdown-menu'}>
          <Menu.Item>
            <CustomIcon
              type={'filter-records'}
              // size={'small'}
            />
            {t('Filter')}
          </Menu.Item>
          <Menu.Item>
            <CustomIcon
              type={'unorderedlist'}
              // size={'small'}
            />
            {t('Filter group')}
          </Menu.Item>
        </Menu>
      </Dropdown>
    </div>
  );
};
