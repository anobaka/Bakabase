import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import { Select } from '@/components/bakaui';

type ExtensionGroup = {
  id: number;
  name: string;
};

type Props = {
  value?: number[];
  onSelectionChange?: (ids: number[]) => void;
};

export default ({ value = [], onSelectionChange }: Props) => {
  const { t } = useTranslation();
  const [extensionGroups, setExtensionGroups] = useState<ExtensionGroup[]>([]);

  // console.log(extensionGroups, value);

  useEffect(() => {
    BApi.extensionGroup.getAllExtensionGroups().then((res) => {
      setExtensionGroups(res.data ?? []);
    });
  }, []);

  return (
    <Select
      selectionMode={'multiple'}
      label={t('Limit file extension groups')}
      placeholder={t('Select from extension groups')}
      dataSource={extensionGroups?.map(l => ({
        label: l.name,
        value: l.id.toString(),
      }))}
      selectedKeys={(value ?? []).map(x => x.toString())}
      onSelectionChange={keys => {
        onSelectionChange?.(Array.from(keys).map(k => parseInt(k as string, 10)));
      }}
    />
  );
};
