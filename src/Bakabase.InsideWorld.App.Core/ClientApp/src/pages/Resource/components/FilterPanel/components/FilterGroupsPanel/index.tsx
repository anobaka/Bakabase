import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import styles from './index.module.scss';
import type { IGroup } from './models';
import { GroupCombinator } from './models';
import FilterGroup from './components/FilterGroup';

interface IProps {
  group?: IGroup;
  onChange?: (group: IGroup) => any;
  portalContainer?: any;
}

export default ({ group: propsGroup, onChange, portalContainer }: IProps) => {
  const { t } = useTranslation();

  const [group, setGroup] = useState<IGroup>(propsGroup ?? { combinator: GroupCombinator.And });

  useUpdateEffect(() => {
    setGroup(propsGroup ?? { combinator: GroupCombinator.And });
  }, [propsGroup]);

  return (
    <div className={`group ${styles.filterGroupsPanel}`}>
      <FilterGroup
        group={group}
        isRoot
        portalContainer={portalContainer}
        onChange={group => {
          setGroup(group);
          onChange?.(group);
        }}
      />
    </div>
  );
};
