import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import styles from './index.module.scss';
import type { IGroup } from './models';
import { GroupCombinator } from './models';
import FilterGroup from './components/FilterGroup';
import TestData from './testData.json';
import CustomIcon from '@/components/CustomIcon';

interface IProps {
  group?: IGroup;
  onChange?: (group: IGroup) => any;
}

export default ({ group: propsGroup, onChange }: IProps) => {
  const { t } = useTranslation();

  const [group, setGroup] = useState<IGroup>(propsGroup ?? TestData ?? {});

  useUpdateEffect(() => {
    onChange?.(group);
  }, [group]);

  return (
    <div className={`group ${styles.filterGroupsPanel}`}>
      <CustomIcon
        type={'filter-records'}
        // size={'small'}
      />
      <FilterGroup
        group={group}
        isRoot
        onChange={group => {
          setGroup(group);
        }}
      />
    </div>
  );
};
