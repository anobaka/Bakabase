import { Button, Dropdown, Menu } from '@alifd/next';
import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import styles from './index.module.scss';
import type { IGroup } from './models';
import { GroupCombinator } from './models';
import FilterGroup from './components/FilterGroup';
import CustomIcon from '@/components/CustomIcon';


export default () => {
  const { t } = useTranslation();

  const [group, setGroup] = useState<IGroup>({
    filters: [{}],
    combinator: GroupCombinator.And,
  });

  return (
    <div className={`group ${styles.filterGroupsPanel}`}>
      <CustomIcon
        type={'filter-records'}
        // size={'small'}
      />
      <FilterGroup group={group} isRoot />
    </div>
  );
};
