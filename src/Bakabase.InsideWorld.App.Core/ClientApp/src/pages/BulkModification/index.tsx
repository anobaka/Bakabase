import { Button, Collapse, Dialog, Icon, Input, Message } from '@alifd/next';
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
import { convertFromApiModel, convertToApiModel } from '@/pages/BulkModification/helpers';
import type { IBulkModification } from '@/pages/BulkModification/components/BulkModification';
import BulkModification from '@/pages/BulkModification/components/BulkModification';


const { Panel } = Collapse;
//
// const resourceModificationResults: IResourceModificationResult[] = [
//   {
//     id: 12345,
//     path: 'D:\\FE Test\\[123132131231】【中文】【作者123]葫芦娃全集',
//     diffs: [
//       ResourceDiffUtils.buildPublisher([
//           {
//             name: 'pub1',
//             children: [
//               {
//                 name: 'sub pub 1-1',
//               },
//               {
//                 name: 'sub pub 1-2',
//                 children: [
//                   {
//                     name: 'sub sub pub 1-2-1',
//                   },
//                   {
//                     name: 'sub sub pub 1-2-2',
//                   },
//                 ],
//               },
//               {
//                 name: 'sub pub 1-3',
//               },
//             ],
//           },
//           {
//             name: 'pub2',
//           },
//           {
//             name: 'pub3',
//           },
//         ], [
//           {
//             name: 'pub2',
//             children: [
//               {
//                 name: 'sub pub 2-1',
//               },
//               {
//                 name: 'sub pub 2-2',
//               },
//               {
//                 name: 'sub pub 2-3',
//               },
//             ],
//           },
//           {
//             name: 'pub3',
//           },
//         ],
//       )!,
//       ResourceDiffUtils.buildPublisher([
//         {
//           name: 'pub2',
//         },
//         {
//           name: 'pub3',
//         }], null,
//       )!,
//       ResourceDiffUtils.buildPublisher(null, [
//         {
//           name: 'pub2',
//         },
//         {
//           name: 'pub3',
//         }],
//       )!,
//     ],
//   },
//   {
//     id: 12345,
//     path: 'D:\\FE Test\\1234dsadasassadassdasdsdsad1234dsadasassadassdasdsdsad1234dsadasassadassdasdsdsad1234dsadasassadassdasdsdsad',
//     diffs: [
//       ResourceDiffUtils.buildMediaLibrary(36, 41)!,
//     ],
//   },
//   {
//     id: 12300,
//     path: 'D:\\FE Test\\123400',
//     diffs: [
//       ResourceDiffUtils.buildName('current name', 'new name')!,
//       ResourceDiffUtils.buildName(null, 'new name')!,
//       ResourceDiffUtils.buildName('current name', null)!,
//     ],
//   },
//   {
//     id: 12301,
//     path: 'D:\\FE Test\\12340011',
//     diffs: [
//       ResourceDiffUtils.buildCustomProperty('custom-property-1', 'current name', 'new name')!,
//       ResourceDiffUtils.buildLanguage(ResourceLanguage.Japanese, ResourceLanguage.Chinese)!,
//       ResourceDiffUtils.buildVolume(null, { index: 5, name: '第五话', title: '小岛秀夫刺杀安倍晋三' })!,
//     ],
//   },
// ];

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
    BApi.publisher.getAllPublishers().then(r => {
      const publishers = r.data || [];
      setDisplayDataSources(s => ({
        ...s,
        [BulkModificationProperty.Publisher]: publishers.reduce<Record<any, any>>((s, t) => {
          s[t.id!] = t.name!;
          return s;
        }, {}),
      }));
      console.log('set display data sources for publishers');
    });
    // @ts-ignore
    BApi.tag.getAllTags({ additionalItems: TagAdditionalItem.GroupName | TagAdditionalItem.PreferredAlias }).then(r => {
      const tags = r.data || [];
      setDisplayDataSources(s => ({
        ...s,
        [BulkModificationProperty.Tag]: tags.reduce<Record<any, any>>((s, t) => {
          // @ts-ignore
          s[t.id!] = new TagDto(t).displayName;
          return s;
        }, {}),
      }));
      console.log('set display data sources for tags');
    });
    BApi.resource.getAllOriginals().then(r => {
      const originals = r.data || [];
      setDisplayDataSources(s => ({
        ...s,
        [BulkModificationProperty.Original]: originals.reduce<Record<any, any>>((s, t) => {
          s[t.id!] = t.name!;
          return s;
        }, {}) }));
    });
    BApi.mediaLibrary.getAllMediaLibraries().then(r => {
      setDisplayDataSources(s => ({
          ...s,
          [BulkModificationProperty.MediaLibrary]: r.data?.reduce<Record<any, any>>((s, t) => {
            s[t.id!] = `[${t.categoryName}] ${t.name!}`;
            return s;
          }, {}),
        }
      ));
      console.log('set display data sources for media libraries');
    });
    loadAllBulkModifications();
  }, []);

  const loadAllBulkModifications = async () => {
    const r = await BApi.bulkModification.getAllBulkModifications();
    const dtos = r.data || [];
    const bms = dtos.map(d => convertFromApiModel(d));
    // @ts-ignore
    setBulkModifications(bms);
  };

  const onChange = async (bm: IBulkModification, save: boolean): Promise<{
    code: number;
    message?: string;
  }> => {
    let rsp = { code: 0, message: undefined };
    if (save) {
      const apiModel = convertToApiModel(bm);
      // @ts-ignore
      rsp = await BApi.bulkModification.putBulkModification(bm.id, apiModel);
    }
    const index = bulkModifications.findIndex(x => x.id == bm.id);
    bulkModifications[index] = bm;
    setBulkModifications([...bulkModifications]);
    return rsp;
  };

  return (
    <div className={'bulk-modification-page'}>
      <div className="header">
        <div className="title">
          Bulk Modification
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
                      <SimpleLabel status={'info'}>
                        {t(BulkModificationStatus[bm.status])}
                      </SimpleLabel>
                    </div>
                    <Button
                      type={'normal'}
                      size={'small'}
                      onClick={e => {
                      e.stopPropagation();
                    }}
                    >{t('Duplicate')}</Button>
                    {processing && (
                      <Icon type={'loading'} size={'small'} />
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
                  </div>
                  <div className="right">
                    <div className="dt" title={t('Last modified at')}>
                      <CustomIcon type={'time'} size={'small'} />
                      {bm.createdAt}
                    </div>
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
    </div>
  );
};
