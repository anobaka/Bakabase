import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { Button } from '@alifd/next';
import styles from './index.module.scss';
import type { IGroup } from './models';
import { GroupCombinator } from './models';
import FilterGroup from './components/FilterGroup';
import TestData from './testData.json';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';

interface IProps {
  group?: IGroup;
  onChange?: (group: IGroup) => any;
}

function convertGroupToDto(group: IGroup) {
  const { combinator, groups, filters } = group;
  return {
    combinator,
    groups: groups?.map(convertGroupToDto),
    filters: filters?.map(filter => {
      return {
        ...filter,
        value: JSON.stringify(filter.value),
      };
    }),
  };
}

export default ({ group: propsGroup, onChange }: IProps) => {
  const { t } = useTranslation();

  const [group, setGroup] = useState<IGroup>(propsGroup ?? TestData ?? {});

  useUpdateEffect(() => {
    onChange?.(group);
  }, [group]);

  return (
    <div className={`group ${styles.filterGroupsPanel}`}>
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
