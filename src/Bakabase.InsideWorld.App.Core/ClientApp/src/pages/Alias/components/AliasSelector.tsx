import { Select } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import i18n from 'i18next';
import { SearchAliases } from '@/sdk/apis';

import './AliasSelector.scss';
import { AliasAdditionalItem } from '@/sdk/constants';

export default ({ onChange, excludeGroupIds = [] }) => {
  const [aliases, setAliases] = useState([]);

  useEffect(() => {
    console.log(excludeGroupIds);
  }, []);

  const onSearch = (v) => {
    SearchAliases({
      name: v,
      additionalItems: AliasAdditionalItem.Candidates,
    }).invoke((a) => {
      const filtered = a.data.filter((b) => excludeGroupIds.every((c) => c != b.groupId));
      console.log(a.data, filtered);
      setAliases(filtered.map((b) => ({
        ...b,
        label: b.name,
        value: b.groupId,
      })));
    });
  };

  const renderAlias = (a) => {
    return (
      <div className={'alias-selector-item'}>
        <div className="preferred">
          {a.name}
        </div>
        <div className="candidates">
          {a.candidates?.map((b) => (
            <div className={'candidate'}>
              {b.name}
            </div>
          ))}
        </div>
      </div>
    );
  };

  return (
    <Select
      showSearch
      className={'alias-selector'}
      dataSource={aliases}
      onChange={onChange}
      itemRender={(a) => renderAlias(a)}
      valueRender={(a) => {
        return renderAlias(a);
      }}
      onSearch={onSearch}
      filterLocal={false}
      placeholder={i18n.t('Search alias')}
    />
  );
};
