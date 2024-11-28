'use strict';
import { useTranslation } from 'react-i18next';
import { DeleteOutlined } from '@ant-design/icons';
import { useCallback, useEffect, useState } from 'react';
import BulkModification from './components/BulkModification';
import { Accordion, AccordionItem, Button, Chip, Modal, Spinner, Tooltip } from '@/components/bakaui';
import type { BulkModification as BulkModificationModel } from '@/pages/BulkModification2/components/BulkModification';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [bulkModifications, setBulkModifications] = useState<BulkModificationModel[]>();

  const loadAllBulkModifications = useCallback(async () => {
    const r = await BApi.bulkModification.getAllBulkModifications();
    setBulkModifications(r.data || []);
  }, []);

  useEffect(() => {
    loadAllBulkModifications();
  }, []);

  return (
    <div>
      <div>
        <Button
          color={'primary'}
          size={'sm'}
          onClick={() => {
            BApi.bulkModification.addBulkModification().then(r => {
              loadAllBulkModifications();
            });
          }}
        >
          {t('Add a bulk modification')}
        </Button>
      </div>
      {bulkModifications ? bulkModifications.length == 0 ? (
        <div className={'flex items-center justify-center min-h-[400px]'}>
          {t('No data')}
        </div>
      ) : (
        <Accordion
          className={'p-0 pt-1'}
          selectionMode={'multiple'}
          variant={'splitted'}
          defaultExpandedKeys={bulkModifications.map(x => x.id.toString())}
        >
          {bulkModifications.map((bm, i) => {
            return (
              <AccordionItem
                key={bm.id}
                //   subtitle={(
                //     <div>
                //       123
                //     </div>
                // )}
                title={(
                  <div className={'flex items-center justify-between'}>
                    <div className={'flex items-center gap-1'}>
                      <div>{bm.name}</div>
                      <Chip size={'sm'}>
                        {t('{{count}} resources related', { count: bm.filteredResourceIds?.length || 0 })}
                      </Chip>
                      <Button
                        size={'sm'}
                        variant={'light'}
                        color={'primary'}
                      >
                        {t('How does this work?')}
                      </Button>
                    </div>
                    <div className={'flex items-center gap-1'}>
                      <Chip
                        size={'sm'}
                        variant={'light'}
                        // color={'secondary'}
                      >
                        {bm.createdAt}
                      </Chip>
                      <Button
                        size={'sm'}
                        variant={'bordered'}
                        onClick={() => {
                          BApi.bulkModification.duplicateBulkModification(bm.id).then(r => {
                            loadAllBulkModifications();
                          });
                        }}
                      >
                        {t('Duplicate')}
                      </Button>
                      <Tooltip
                        content={t(`Click to ${bm.isActive ? 'disable' : 'enable'}`)}
                      >
                        <Button
                          size={'sm'}
                          color={bm.isActive ? 'success' : 'warning'}
                          variant={'light'}
                          onClick={() => {
                            BApi.bulkModification.patchBulkModification(bm.id, { isActive: !bm.isActive }).then(r => {
                              loadAllBulkModifications();
                            });
                          }}
                        >
                          {t(bm.isActive ? 'Enabled' : 'Disabled')}
                        </Button>
                      </Tooltip>
                      <Button
                        size={'sm'}
                        color={'danger'}
                        isIconOnly
                        variant={'light'}
                        onClick={() => {
                          createPortal(
                            Modal, {
                              defaultVisible: true,
                              title: t('Delete bulk modification'),
                              children: t('Are you sure to delete this bulk modification?'),
                              onOk: async () => {
                                await BApi.bulkModification.deleteBulkModification(bm.id);
                                loadAllBulkModifications();
                              },
                            },
                          );
                        }}
                      >
                        <DeleteOutlined className={'text-base'} />
                      </Button>
                    </div>
                  </div>
                )}
              >
                <BulkModification
                  bm={bm}
                  onChange={newBm => setBulkModifications(
                    bulkModifications.map(b => (b.id == newBm.id ? newBm : b)),
                  )}
                />
              </AccordionItem>
            );
          })}
        </Accordion>
      ) : (
        <div className={'flex items-center justify-center min-h-[400px]'}>
          <Spinner size={'lg'} />
        </div>
      )}
    </div>
  );
};
