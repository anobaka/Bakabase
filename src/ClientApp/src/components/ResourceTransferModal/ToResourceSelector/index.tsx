import { useTranslation } from 'react-i18next';
import { useAsyncList } from '@react-stately/data';
import { useState } from 'react';
import { Autocomplete, AutocompleteItem } from '@/components/bakaui';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import BApi from '@/sdk/BApi';
import { buildLogger } from '@/components/utils';

type Props = {
  onSelect: (id: number) => any;
};

type Item = {
  fileName: string;
  path?: string;
  id: number;
};

const log = buildLogger('ToResourceSelector');

export default ({ onSelect }: Props) => {
  const { t } = useTranslation();

  const targetResourceCandidates = useAsyncList<Item>({
    async load({
                 signal,
                 filterText,
               }) {
      if (filterText != undefined && filterText.length > 0) {
        const trim = filterText.trim();
        const res = await BApi.resource.searchResourcePaths({ keyword: trim }, { signal });
        const data = res.data || [];
        const isOverflow = data.length > 20;
        const listItems: Item[] = data.slice(0, 20).map(d => ({ id: d.id, path: d.path, fileName: d.fileName }));
        if (isOverflow) {
          listItems.push({
            id: 0,
            fileName: t('20 results can be shown at most, please refine your search'),
          });
        }

        return {
          items: listItems,
        };
      } else {
        return {
          items: [],
        };
      }
    },
  });

  return (
    <Autocomplete
      label={t('Input keyword of the resource path to select the target resource')}
      inputValue={targetResourceCandidates.filterText}
      fullWidth
      onInputChange={targetResourceCandidates.setFilterText}
      items={targetResourceCandidates.items}
      radius={'none'}
      size={'sm'}
      isLoading={targetResourceCandidates.isLoading}
      listboxProps={{
        emptyContent: t('Can not find any resource'),
      }}
      onSelectionChange={key => {
        log(key);
        const id = key as number;
        if (id) {
          onSelect(id);
        }
      }}
    >
      {(rc: Item) => {
        const isDisabled = rc.id == 0;
        return (
          <AutocompleteItem
            key={rc.id}
            title={rc.path}
            isDisabled={isDisabled}
            className={`${isDisabled ? 'opacity-60' : ''}`}
          >
            {rc.fileName}
          </AutocompleteItem>
        );
      }}
    </Autocomplete>
  );
};
