import type { CSSProperties } from 'react';
import { useCallback, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Variables from './Variables';
import Processes from './Processes';
import { Button, Divider, Tab, Tabs } from '@/components/bakaui';
import FilterGroupsPanel from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel';
import type { ResourceSearchFilterGroup } from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import type { IBulkModificationProcess } from '@/pages/BulkModification/components/BulkModification';
import FilteredResourcesDialog from '@/pages/BulkModification/components/BulkModification/FilteredResourcesDialog';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import BApi from '@/sdk/BApi';

export type BulkModification = {
  id: number;
  name: string;
  isActive: boolean;
  createdAt: string;
  variables?: BulkModificationVariable[];
  filter?: ResourceSearchFilterGroup;
  processes?: IBulkModificationProcess[];
  filteredResourceIds?: number[];
};

type Props = {
  bm: BulkModification;
  onChange: (bm: BulkModification) => any;
};

type BlockKey = 'Filters' | 'Variables' | 'Processes' | 'Result';
const Blocks: BlockKey[] = ['Filters', 'Variables', 'Processes', 'Result'];

export default ({
                  bm,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();
  const [selectedTab, setSelectedTab] = useState<BlockKey>();

  const reload = useCallback(async () => {
    const r = await BApi.bulkModification.getBulkModification(bm.id);
    onChange(r.data!);
  }, [onChange]);

  return (
    <div className={'flex items-start gap-2'}>
      <Tabs
        selectedKey={selectedTab}
        isVertical
        onSelectionChange={key => {
          setSelectedTab(key as BlockKey);
        }}
      >
        {Blocks.map(b => {
          return (
            <Tab key={b} title={t(b)} />
          );
        })}
      </Tabs>
      <div className={'flex flex-col gap-2 grow'}>
        {Blocks.map((bk, i) => {
          let blockInner: any;
          switch (bk) {
            case 'Filters':
              blockInner = (
                <div>
                  <div>
                    <FilterGroupsPanel
                      group={bm.filter}
                      onChange={g => {
                        bm.filter = g;
                        BApi.bulkModification.patchBulkModification(bm.id, { filter: g });
                      }}
                    />
                  </div>
                  <div className={'flex items-center gap-2'}>
                    <Button
                      size={'sm'}
                      color={'primary'}
                      onClick={() => {
                        BApi.bulkModification.filterResourcesInBulkModification(bm.id).then(r => {
                          reload();
                        });
                      }}
                      variant={'ghost'}
                    >
                      {t('Filter(Verb)')}
                    </Button>
                    <div className={''}>
                      {t('{{count}} resources have been filtered out', { count: bm.filteredResourceIds?.length || 0 })}
                    </div>
                    {/* {bm.filteredAt && ( */}
                    {/* <div className={'filtered-at'}>{t('Filtered at {{datetime}}', { datetime: bm.filteredAt })}</div> */}
                    {/* )} */}
                    {/* {bm.filteredResourceIds && bm.filteredResourceIds.length > 0 && ( */}
                    <Button
                      color={'primary'}
                      variant={'light'}
                      size={'sm'}
                      onClick={() => {
                        FilteredResourcesDialog.show({
                          bmId: bm.id!,
                        });
                      }}
                    >{t('Check all the resources that have been filtered out')}</Button>
                    {/* )} */}
                  </div>

                </div>
              );
              break;
            case 'Variables':
              blockInner = (
                <Variables
                  variables={bm.variables}
                  onChange={vs => {
                    bm.variables = vs;
                    BApi.bulkModification.patchBulkModification(bm.id, {
                      variables: vs.map(v => ({
                        ...v,
                        preprocesses: JSON.stringify(v.preprocesses),
                      })),
                    }).then(r => {
                      reload();
                    });
                  }}
                />
              );
              break;
            case 'Processes':
              blockInner = (
                <Processes />
              );
              break;
            case 'Result':
              break;
          }
          const style: CSSProperties = {};
          if (bk == selectedTab) {
            style.backgroundColor = 'var(--bakaui-overlap-background)';
          }
          return (
            <>
              <div
                style={style}
                onClick={() => {
                  setSelectedTab(bk);
                }}
              >
                {/* <div>{t(bk)}</div> */}
                {blockInner}
              </div>
              {i != Blocks.length - 1 && <Divider orientation={'horizontal'} />}
            </>
          );
        })}
      </div>
    </div>
  );
};
