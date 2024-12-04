import { useCallback, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Variables from './Variables';
import Processes from './Processes';
import { Button, Chip, Divider, Modal } from '@/components/bakaui';
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
  appliedAt?: string;
  resourceDiffCount: number;
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

  const [calculatingDiffs, setCalculatingDiffs] = useState(false);

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
                    {/* <Button */}
                    {/*   color={'primary'} */}
                    {/*   variant={'light'} */}
                    {/*   size={'sm'} */}
                    {/*   onClick={() => { */}
                    {/*     FilteredResourcesDialog.show({ */}
                    {/*       bmId: bm.id!, */}
                    {/*     }); */}
                    {/*   }} */}
                    {/* >{t('Check all the resources that have been filtered out')}</Button> */}
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
                    isLoading={calculatingDiffs}
                    onClick={() => {
                      setCalculatingDiffs(true);
                      BApi.bulkModification.previewBulkModification(bm.id).then(r => {
                        reload();
                      }).finally(() => {
                        setCalculatingDiffs(false);
                      });
                    }}
                  >
                    {t('Calculate diffs')}
                  </Button>
                  {bm.resourceDiffCount > 0 && (
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
                      {t('Check {{count}} diffs', { count: bm.resourceDiffCount })}
                    </Button>
                  )}
                </div>
              );
              break;
            case 'Final':
              blockInner = (
                <div className={'flex items-center gap-1'}>
                  <Button
                    size={'sm'}
                    color={'primary'}
                    isDisabled={!bm.processes || bm.processes.length === 0 || bm.resourceDiffCount == 0}
                    onClick={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t('Apply bulk modification'),
                        children: t('Please check diffs before applying, and changed value may not be reverted perfectly in some situations,  are you sure to apply?'),
                        onOk: async () => {
                          await BApi.bulkModification.applyBulkModification(bm.id);
                          reload();
                        },
                      });
                    }}
                  >
                    {t('Apply diffs')}
                  </Button>
                  {bm.appliedAt && (
                    <Button
                      size={'sm'}
                      color={'warning'}
                      variant={'flat'}
                      onClick={() => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t('Revert bulk modification'),
                          children: t('Are you sure to revert the bulk modification?'),
                          onOk: async () => {
                            await BApi.bulkModification.revertBulkModification(bm.id);
                            reload();
                          },
                        });
                      }}
                    >
                      {t('Revert diffs')}
                    </Button>
                  )}
                </div>
              );
              break;
          }
          return (
            <>
              <div className={'flex gap-4 items-center'}>
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
