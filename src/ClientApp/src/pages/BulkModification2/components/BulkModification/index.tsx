import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import Variables from './Variables';
import Processes from './Processes';
import { Button, Chip, Divider } from '@/components/bakaui';
import FilterGroupsPanel from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel';
import type { ResourceSearchFilterGroup } from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import FilteredResourcesDialog from '@/pages/BulkModification/components/BulkModification/FilteredResourcesDialog';
import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import BApi from '@/sdk/BApi';

export type BulkModification = {
  id: number;
  name: string;
  isActive: boolean;
  createdAt: string;
  variables?: BulkModificationVariable[];
  filter?: ResourceSearchFilterGroup;
  processes?: BulkModificationProcess[];
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

  const reload = useCallback(async () => {
    const r = await BApi.bulkModification.getBulkModification(bm.id);
    onChange(r.data!);
  }, [onChange]);

  return (
    <div className={'flex items-start gap-2'}>
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
                <Processes
                  variables={bm.variables}
                  processes={bm.processes}
                  onChange={ps => {
                    bm.processes = ps;
                    BApi.bulkModification.patchBulkModification(bm.id, {
                      processes: ps.map(v => ({
                        ...v,
                        steps: JSON.stringify(v.steps),
                      })),
                    }).then(r => {
                      reload();
                    });
                  }}
                />
              );
              break;
            case 'Result':
              blockInner = (
                <div className={'flex items-center gap-1'}>
                  <Button
                    size={'sm'}
                    variant={'bordered'}
                    color={'primary'}
                  >
                    {t('Calculate diffs')}
                  </Button>
                  {bm.filteredResourceIds && (
                    <Button
                      size={'sm'}
                      variant={'light'}
                      color={'primary'}
                    >
                      {t('Check diffs')}
                    </Button>
                  )}
                  <Button
                    size={'sm'}
                    color={'primary'}
                  >
                    {t('Apply diffs')}
                  </Button>
                  <Button
                    size={'sm'}
                    color={'warning'}
                    variant={'bordered'}
                  >
                    {t('Revert diffs')}
                  </Button>
                </div>
              );
              break;
          }
          return (
            <>
              <div className={'flex items-start gap-4'}>
                <div className={'w-[80px] text-right'}>
                  <Chip
                    radius={'sm'}
                  >{t(bk)}</Chip>
                </div>
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
