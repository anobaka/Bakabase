import { useTranslation } from 'react-i18next';
import { useAsyncList } from '@react-stately/data';
import { useState } from 'react';
import { ReloadOutlined } from '@ant-design/icons';
import { Autocomplete, AutocompleteItem, Button, Chip } from '@/components/bakaui';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import BApi from '@/sdk/BApi';
import { buildLogger } from '@/components/utils';

type Props = {
  onSelect: (id: number) => any;
  fromResourcePath: string;
};

type Item = {
  fileName: string;
  path?: string;
  id: number;
};

const log = buildLogger('ToResourceSelector');

export default ({
                  onSelect,
                  fromResourcePath,
                }: Props) => {
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
        const listItems: Item[] = data.slice(0, 20).map(d => ({
          id: d.id,
          path: d.path,
          fileName: d.fileName,
        }));
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
    initialFilterText: fromResourcePath,
  });

  // log('default input value');

  return (
    <div className={'mb-4'}>
      <Autocomplete
        isRequired
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
          const id = parseInt(key as string, 10);
          if (id) {
            onSelect(id);
          }
        }}
        description={t('You may need modify the default keyword to search the expected resources')}
      >
        {(rc: Item) => {
          const isFromResource = fromResourcePath == rc.path;
          const isDisabled = rc.id == 0 || isFromResource;
          return (
            <AutocompleteItem
              key={rc.id}
              title={rc.path}
              isDisabled={isDisabled}
              className={`${isDisabled ? 'opacity-60' : ''}`}
              startContent={isFromResource
                ? <Chip
                    size={'sm'}
                    radius={'sm'}
                >{t('Current path')}</Chip> : undefined}
            >
              {rc.fileName}
            </AutocompleteItem>
          );
        }}
      </Autocomplete>
      <Button
        size={'sm'}
        variant={'light'}
        onClick={() => {
          targetResourceCandidates.setFilterText(fromResourcePath);
        }}
      >
        <ReloadOutlined className={'text-base'} />
        {t('Reset keyword to path of from resource')}
      </Button>
    </div>
  );
};
