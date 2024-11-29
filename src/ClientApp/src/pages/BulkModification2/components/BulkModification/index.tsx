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
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import DiffsModal from '@/pages/BulkModification2/components/BulkModification/DiffsModal';

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

type BlockKey = 'Filters' | 'Variables' | 'Processes' | 'Diffs' | 'Final';
const Blocks: BlockKey[] = ['Filters', 'Variables', 'Processes', 'Diffs', 'Final'];

export default ({
                  bm,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

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
            case 'Diffs':
              blockInner = (
                <div className={'flex items-center gap-1'}>
                  <Button
                    size={'sm'}
                    variant={'bordered'}
                    color={'primary'}
                    onClick={() => {
                      BApi.bulkModification.previewBulkModification(bm.id).then(r => {
                        reload();
                      });
                    }}
                  >
                    {t('Calculate diffs')}
                  </Button>
                  <Button
                    size={'sm'}
                    variant={'light'}
                    color={'primary'}
                    onClick={() => {
                      createPortal(
                        DiffsModal, {
                          bmId: bm.id,
                        },
                      );
                    }}
                  >
                    {t('Check diffs')}
                  </Button>
                </div>
              );
              break;
            case 'Final':
              blockInner = (
                <div className={'flex items-center gap-1'}>
                  <Button
                    size={'sm'}
                    color={'primary'}
                  >
                    {t('Apply diffs')}
                  </Button>
                  <Button
                    size={'sm'}
                    color={'warning'}
                    variant={'flat'}
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
