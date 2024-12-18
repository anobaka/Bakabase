'use strict';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import type { ResourceSearchFilterGroup } from './models';
import { GroupCombinator } from './models';
import FilterGroup from './FilterGroup';
import type { ResourceTag } from '@/sdk/constants';
import { buildLogger } from '@/components/utils';

interface IProps {
  group?: ResourceSearchFilterGroup;
  onChange?: (group: ResourceSearchFilterGroup) => any;
  portalContainer?: any;
  tags?: ResourceTag[];
  onTagsChange?: (tags: ResourceTag[]) => any;
}

const log = buildLogger('FilterGroupsPanel');

export default ({
                  group: propsGroup,
                  onChange,
                  portalContainer,
                  tags,
                  onTagsChange,
                }: IProps) => {
  const { t } = useTranslation();

  const [group, setGroup] = useState<ResourceSearchFilterGroup>(propsGroup ?? {
    combinator: GroupCombinator.And,
    disabled: false,
  });

  useEffect(() => {
  }, []);

  useUpdateEffect(() => {
    setGroup(propsGroup ?? {
      combinator: GroupCombinator.And,
      disabled: false,
    });
  }, [propsGroup]);

  return (
    <div className={'group flex flex-wrap gap-2 item-center'}>
      <FilterGroup
        group={group}
        isRoot
        portalContainer={portalContainer}
        onChange={group => {
          setGroup(group);
          onChange?.(group);
        }}
        tags={tags}
        onTagsChange={onTagsChange}
      />
    </div>
  );
};
