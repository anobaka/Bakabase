import React, { useRef, useState } from 'react';
import ReactJson from 'react-json-view';
import type {
  ResourceSearchFilterGroup,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import {
  GroupCombinator,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import FilterGroupsPanel from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel';
import { Button } from '@/components/bakaui';

export default () => {
  const [group, setGroup] = useState<ResourceSearchFilterGroup>({ combinator: GroupCombinator.And });

  return (
    <div>
      <div>
        <FilterGroupsPanel
          group={group}
          onChange={g => setGroup(g)}
        />
      </div>
      <div>
        <ReactJson
          name={'Data'}
          src={group}
          theme={'monokai'}
          collapsed
        />
      </div>
    </div>
  );
};
