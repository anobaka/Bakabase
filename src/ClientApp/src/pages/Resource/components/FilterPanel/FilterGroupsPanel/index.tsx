import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import type { ResourceSearchFilterGroup } from './models';
import { GroupCombinator } from './models';
import FilterGroup from './FilterGroup';

interface IProps {
  group?: ResourceSearchFilterGroup;
  onChange?: (group: ResourceSearchFilterGroup) => any;
  portalContainer?: any;
}

export default ({
                  group: propsGroup,
                  onChange,
                  portalContainer,
                }: IProps) => {
  const { t } = useTranslation();

  const [group, setGroup] = useState<ResourceSearchFilterGroup>(propsGroup ?? { combinator: GroupCombinator.And });

  useEffect(() => {
  }, []);

  useUpdateEffect(() => {
    setGroup(propsGroup ?? { combinator: GroupCombinator.And });
  }, [propsGroup]);

  return (
    <div className={'group flex flex-wrap gap-2 item-center mt-2'}>
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
