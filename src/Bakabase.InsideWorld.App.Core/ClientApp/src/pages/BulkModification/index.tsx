import { Button, Collapse, Dialog, Icon, Input, Loading, Message } from '@alifd/next';
import SimpleLabel from '@/components/SimpleLabel';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import { useTranslation } from 'react-i18next';
import { BulkModificationProperty, BulkModificationStatus, TagAdditionalItem } from '@/sdk/constants';
import { useEffect, useState } from 'react';
import BApi from '@/sdk/BApi';
import { useTour } from '@reactour/tour';
import ClickableIcon from '@/components/ClickableIcon';
import { Tag as TagDto } from '@/core/models/Tag';
import { convertFromApiModel } from '@/pages/BulkModification/helpers';
import type { IBulkModification } from '@/pages/BulkModification/components/BulkModification';
import BulkModification from '@/pages/BulkModification/components/BulkModification';


const { Panel } = Collapse;

const BmStatusLabelStatusMap = {
  [BulkModificationStatus.Processing]: 'warning',
  [BulkModificationStatus.Closed]: 'default',
};

export default () => {
  const { t } = useTranslation();
  const [processing, setProcessing] = useState(false);

  const [bulkModifications, setBulkModifications] = useState<IBulkModification[]>([]);

  const [displayDataSources, setDisplayDataSources] = useState<{ [property in BulkModificationProperty]?: Record<any, any> }>({});

  console.log('[BulkModifications]', bulkModifications, displayDataSources);

  const [expandedKeys, setExpandedKeys] = useState<string[]>([]);
  const {
    isOpen,
    currentStep,
    steps,
    setIsOpen,
    setCurrentStep,
    setSteps,
  } = useTour();

  useEffect(() => {
    console.log(displayDataSources);
  }, [displayDataSources]);

  useEffect(() => {
    init();
  }, []);

  const init = async () => {
    setProcessing(true);
    try {
      const publishers = (await BApi.publisher.getAllPublishers()).data ?? [];
      // @ts-ignore
      const tags = (await BApi.tag.getAllTags({ additionalItems: TagAdditionalItem.GroupName | TagAdditionalItem.PreferredAlias })).data ?? [];
      const originals = (await BApi.resource.getAllOriginals()).data ?? [];
      const mediaLibraries = (await BApi.mediaLibrary.getAllMediaLibraries()).data ?? [];
      setDisplayDataSources({
        [BulkModificationProperty.Publisher]: publishers.reduce<Record<any, any>>((s, t) => {
          s[t.id!] = t.name!;
          return s;
        }, {}),
        [BulkModificationProperty.Tag]: tags.reduce<Record<any, any>>((s, t) => {
          // @ts-ignore
          s[t.id!] = new TagDto(t).displayName;
          return s;
        }, {}),
        [BulkModificationProperty.Original]: originals.reduce<Record<any, any>>((s, t) => {
          s[t.id!] = t.name!;
          return s;
        }, {}),
        [BulkModificationProperty.MediaLibrary]: mediaLibraries.reduce<Record<any, any>>((s, t) => {
          s[t.id!] = `[${t.categoryName}] ${t.name!}`;
          return s;
        }, {}),
      });
      await loadAllBulkModifications();
    } finally {
      setProcessing(false);
    }
  };

  const loadAllBulkModifications = async () => {
    const r = await BApi.bulkModification.getAllBulkModifications();
    const dtos = r.data || [];
    const bms = dtos.map(d => convertFromApiModel(d));
    // @ts-ignore
    setBulkModifications(bms);
  };

  const onChange = async (bm: IBulkModification) => {
    const index = bulkModifications.findIndex(x => x.id == bm.id);
    bulkModifications[index] = bm;
    setBulkModifications([...bulkModifications]);
  };

  return (
    <div className={'bulk-modification-page'}>
      <div className="header">
        <div className="title">
          {t('Bulk modification')}
        </div>
        <Button
          type={'primary'}
          size={'small'}
          onClick={() => {
            let name: string;
            Dialog.show({
              title: t('Creating a bulk modification'),
              content: (
                <Input
                  placeholder={t('Please input a name')}
                  style={{ width: 500 }}
                  trim
                  onChange={v => name = v}
                />
              ),
              v2: true,
              width: 'auto',
              closeMode: ['close', 'mask', 'esc'],
              onOk: async () => {
                if (name == undefined || name.length == 0) {
                  return Message.error(t('Name is required'));
                }
                const r = await BApi.bulkModification.createBulkModification({ name });
                if (!r.code) {
                  await loadAllBulkModifications();
                }
              },
            });
          }}
        >{t('Create')}</Button>
      </div>
      {/* <div className="bulk-modifications-container"> */}
      <Loading visible={processing} className={'bulk-modifications-container'}>
        <Collapse
          key={bulkModifications?.length}
          className={'bulk-modifications'}
          expandedKeys={expandedKeys}
          onExpand={keys => {
            setExpandedKeys(keys);
          }}
        >
          {bulkModifications?.map((bm, i) => {
            const expanded = expandedKeys.includes(bm.id.toString());
            return (
              <Panel
                key={bm.id}
                className={`bulk-modification-${bm.id} bulk-modification`}
                title={(
                  <div className={'title-bar'}>
                    <div className="left">
                      <div className="name">{bm.name}</div>
                      <div className="resource-count">
                        涉及<span>{bm.filteredResourceIds?.length ?? 0}</span>个资源
                      </div>
                      <div className="status">
                        {/* @ts-ignore */}
                        <SimpleLabel status={BmStatusLabelStatusMap[bm.status]}>
                          {t(BulkModificationStatus[bm.status])}
                        </SimpleLabel>
                      </div>
                      <Button
                        type={'normal'}
                        size={'small'}
                        onClick={e => {
                          e.stopPropagation();
                          Dialog.confirm({
                            title: t('Duplicating a bulk modification'),
                            content: t('A new bulk modification will be created from current selection'),
                            v2: true,
                            width: 'auto',
                            onOk: async () => {
                              await BApi.bulkModification.duplicateBulkModification(bm.id);
                              await loadAllBulkModifications();
                            },
                          });
                        }}
                      >{t('Duplicate')}</Button>
                      {bm.status != BulkModificationStatus.Closed && (
                        <Button
                          type={'normal'}
                          warning
                          size={'small'}
                          onClick={e => {
                            e.stopPropagation();
                            Dialog.confirm({
                              title: t('Close a bulk modification'),
                              content: t('You will not be able to operate on a closed bulk modification'),
                              v2: true,
                              width: 'auto',
                              onOk: async () => {
                                await BApi.bulkModification.closeBulkModification(bm.id);
                                await loadAllBulkModifications();
                              },
                            });
                          }}
                        >{t('Close')}</Button>
                      )}
                      {expanded && (
                        <Button
                          type={'primary'}
                          text
                          size={'small'}
                          onClick={e => {
                            e.stopPropagation();
                            setSteps!([
                              {
                                selector: `.bulk-modification-${bm.id} .filters-panel`,
                                content: t('You can set any combination of criteria to filter the resources that you need to modify in bulk'),
                              },
                              {
                                selector: `.bulk-modification-${bm.id} .variables-panel`,
                                content: t('You can set some variables and use them in processes'),
                              },
                              {
                                selector: `.bulk-modification-${bm.id} .processes-panel`,
                                content: t('To modify the properties of filtered resources, you should set at least one process'),
                              },
                              {
                                selector: `.bulk-modification-${bm.id} .result-panel`,
                                content: t('You can preview the result then apply all changes'),
                              },
                            ]);
                            setCurrentStep(0);
                            setIsOpen(o => true);
                          }}
                        >
                          {t('How does this work?')}
                        </Button>
                      )}
                      {processing && (
                        <Icon type={'loading'} size={'small'} />
                      )}
                    </div>
                    <div className="right">
                      <div className="dt" title={t('Last modified at')}>
                        <CustomIcon type={'time'} size={'small'} />
                        {bm.createdAt}
                      </div>
                      <ClickableIcon
                        colorType={'danger'}
                        type={'delete'}
                        onClick={e => {
                          e.stopPropagation();
                          Dialog.confirm({
                            title: t('Deleting this bulk modification'),
                            content: t('Are you sure to delete this bulk modification?'),
                            v2: true,
                            width: 'auto',
                            onOk: async () => {
                              await BApi.bulkModification.deleteBulkModification(bm.id);
                              await loadAllBulkModifications();
                            },
                          });
                        }}
                      />
                    </div>
                  </div>
                )}
              >
                <BulkModification
                  bm={bm}
                  onChange={onChange}
                  displayDataSources={displayDataSources}
                />
              </Panel>
            );
          })}
        </Collapse>
      </Loading>
      {/* </div> */}
    </div>
  );
};
