import { Button, Collapse, Icon } from '@alifd/next';
import SimpleLabel from '@/components/SimpleLabel';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import { useTranslation } from 'react-i18next';
import type { BulkModificationFilterOperation } from '@/sdk/constants';
import {
  BulkModificationDiffType,
  BulkModificationFilterGroupOperation,
  BulkModificationProperty,
  BulkModificationStatus,
  ResourceLanguage,
  TagAdditionalItem,
} from '@/sdk/constants';
import { useEffect, useState } from 'react';
import FilterGroup from '@/pages/BulkModification/components/FilterGroup';
import BApi from '@/sdk/BApi';
import ProcessDialog from '@/pages/BulkModification/components/ProcessDialog';
import type { IVariable } from '@/pages/BulkModification/components/Variables';
import Variables from '@/pages/BulkModification/components/Variables';
import ProcessDemonstrator from '@/pages/BulkModification/components/ProcessDemonstrator';
import { useTour } from '@reactour/tour';
import ClickableIcon from '@/components/ClickableIcon';
import testBmsJson from './testBms.json';
import type { IResourceDiff } from '@/pages/BulkModification/components/ResourceDiff';
import ResourceDiff from './components/ResourceDiff';
import { Tag as TagDto } from '@/core/models/Tag';
import { ResourceDiffUtils } from '@/pages/BulkModification/components/ResourceDiff/models';


const { Panel } = Collapse;

export interface IBulkModificationFilter {
  property?: BulkModificationProperty;
  propertyKey?: string;

  operation?: BulkModificationFilterOperation;

  target?: string;
}

export interface IBulkModificationFilterGroup {
  operation: BulkModificationFilterGroupOperation;
  filters?: IBulkModificationFilter[];
  groups?: IBulkModificationFilterGroup[];
}

export interface IBulkModificationProcess {
  property?: BulkModificationProperty;
  propertyKey?: string;
  value?: any;
}

interface IBulkModification {
  id: number;
  name: string;
  status: BulkModificationStatus;
  createdAt: string;
  variables?: IVariable[];
  filter?: IBulkModificationFilterGroup;
  processes?: IBulkModificationProcess[];
}

interface IResourceModificationResult {
  id: number;
  path: string;
  diffs: IResourceDiff[];
}

const resourceModificationResults: IResourceModificationResult[] = [
  {
    id: 12345,
    path: 'D:\\FE Test\\[123132131231】【中文】【作者123]葫芦娃全集',
    diffs: [
      ResourceDiffUtils.buildPublisher([
          {
            name: 'pub1',
            children: [
              {
                name: 'sub pub 1-1',
              },
              {
                name: 'sub pub 1-2',
                children: [
                  {
                    name: 'sub sub pub 1-2-1',
                  },
                  {
                    name: 'sub sub pub 1-2-2',
                  },
                ],
              },
              {
                name: 'sub pub 1-3',
              },
            ],
          },
          {
            name: 'pub2',
          },
          {
            name: 'pub3',
          },
        ], [
          {
            name: 'pub2',
            children: [
              {
                name: 'sub pub 2-1',
              },
              {
                name: 'sub pub 2-2',
              },
              {
                name: 'sub pub 2-3',
              },
            ],
          },
          {
            name: 'pub3',
          },
        ],
      )!,
      ResourceDiffUtils.buildPublisher([
        {
          name: 'pub2',
        },
        {
          name: 'pub3',
        }], null,
      )!,
      ResourceDiffUtils.buildPublisher(null, [
        {
          name: 'pub2',
        },
        {
          name: 'pub3',
        }],
      )!,
    ],
  },
  {
    id: 12345,
    path: 'D:\\FE Test\\1234',
    diffs: [
      ResourceDiffUtils.buildMediaLibrary(36, 41)!,
    ],
  },
  {
    id: 12300,
    path: 'D:\\FE Test\\123400',
    diffs: [
      ResourceDiffUtils.buildName('current name', 'new name')!,
      ResourceDiffUtils.buildName(null, 'new name')!,
      ResourceDiffUtils.buildName('current name', null)!,
    ],
  },
  {
    id: 12301,
    path: 'D:\\FE Test\\12340011',
    diffs: [
      ResourceDiffUtils.buildCustomProperty('custom-property-1', 'current name', 'new name')!,
      ResourceDiffUtils.buildLanguage(ResourceLanguage.Japanese, ResourceLanguage.Chinese)!,
      ResourceDiffUtils.buildVolume(null, { index: 5, name: '第五话', title: '小岛秀夫刺杀安倍晋三' })!,
    ],
  },
];

export default () => {
  const { t } = useTranslation();
  const [processing, setProcessing] = useState(false);

  const [bulkModifications, setBulkModifications] = useState<IBulkModification[]>(testBmsJson);

  const [displayDataSources, setDisplayDataSources] = useState<{ [property in BulkModificationProperty]?: Record<any, any> }>({});

  console.log('[BulkModifications]', bulkModifications, displayDataSources);

  const [expandedKeys, setExpandedKeys] = useState<string[]>(['5']);
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
  }, []);

  return (
    <div className={'bulk-modification-page'}>
      <div className="header">
        <div className="title">
          Bulk Modification
          <ClickableIcon
            type={'question-circle'}
            colorType={'normal'}
            onClick={() => {
              setSteps!([
                {
                  selector: '.filters-panel',
                  content: t('You can set any combination of criteria to filter the resources that you need to modify in bulk'),
                },
                {
                  selector: '.variables-panel',
                  content: t('You can set some variables and use them in processes'),
                },
                {
                  selector: '.processes-panel',
                  content: t('To modify the properties of filtered resources, you should set at least one process'),
                },
                {
                  selector: '.result-panel',
                  content: t('You can preview the result then apply all changes'),
                },
              ]);
              setCurrentStep(0);
              setIsOpen(o => true);
            }}
          />
        </div>
        <Button
          type={'primary'}
          size={'small'}
        >{t('Create a bulk modification')}</Button>
      </div>
      <Collapse className={'bulk-modifications'} expandedKeys={expandedKeys} onExpand={keys => setExpandedKeys(keys)}>
        {bulkModifications.map((bm, i) => {
          return (
            <Panel
              key={bm.id}
              className={'bulk-modification'}
              title={(
                <div className={'title-bar'}>
                  <div className="left">
                    <div className="name">{bm.name}</div>
                    <div className="resource-count">
                      涉及<span>38275</span>个资源
                    </div>
                    <div className="status">
                      <SimpleLabel status={'info'}>
                        {t(BulkModificationStatus[bm.status])}
                      </SimpleLabel>
                    </div>
                    <Button type={'normal'} size={'small'}>{t('Duplicate')}</Button>
                    {processing && (
                      <Icon type={'loading'} size={'small'} />
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
              <div className="filters-panel">
                <div className="title">
                  {t('Filters')}
                </div>
                <div className="content">
                  <div className="filters">
                    <FilterGroup
                      onChange={v => {
                        setProcessing(true);
                        BApi.bulkModification.putBulkModification({
                          ...bm,
                          filter: v,
                        }).finally(() => {
                          setProcessing(false);
                        });
                      }}
                      group={bm.filter || { operation: BulkModificationFilterGroupOperation.And }}
                      isRoot
                    />
                  </div>
                  <div className="opts">
                    <Button type={'primary'} size={'small'}>筛选</Button>
                    <div className={'resource-count'}>
                      总计筛选出<span>38275</span>个资源
                    </div>
                    <Button type={'primary'} text size={'small'}>查看完整筛选结果</Button>
                  </div>
                </div>
              </div>
              <div className="variables-panel">
                <div className="title">
                  {t('Variables')}
                </div>
                <Variables variables={bm.variables} />
              </div>
              <div className="processes-panel">
                <div className="title">
                  {t('Processes')}
                </div>
                <div className="content">
                  <div className="processes">
                    {bm.processes?.map((p, j) => {
                      return (
                        <ProcessDemonstrator
                          dataSources={displayDataSources[p.property!]}
                          process={p}
                          index={j}
                          variables={bm.variables}
                          onChange={p => {
                            bulkModifications[i].processes![j] = p;
                            setBulkModifications([
                              ...bulkModifications,
                            ]);
                          }}
                        />
                      );
                    })}
                  </div>
                  <div className="opts">
                    <Button
                      type={'normal'}
                      size={'small'}
                      onClick={() => {
                        ProcessDialog.show({
                          variables: bm.variables,
                          onSubmit: pv => {
                            console.log(pv, 1111);
                            bulkModifications[i].processes = [
                              ...(bm.processes || []),
                              pv,
                            ];
                            setBulkModifications([
                              ...bulkModifications,
                            ]);
                          },
                        });
                      }}
                    >添加修改步骤</Button>
                  </div>
                </div>
              </div>
              <div className="result-panel">
                <div className="title">
                  {t('Result')}
                </div>
                <div className="content">
                  <div className="opts">
                    <Button type={'normal'} size={'small'}>预览修改结果</Button>
                    <Button type={'primary'} size={'small'}>执行</Button>
                  </div>
                  <div className="preview">
                    {resourceModificationResults.map(r => {
                      return (
                        <div className="item">
                          <SimpleLabel status={'default'}>{r.id}</SimpleLabel>
                          <div className="path">
                            {r.path}
                          </div>
                          <div className="diffs">
                            {r.diffs.map(d => {
                              return (
                                <ResourceDiff
                                  diff={d}
                                  displayDataSources={displayDataSources}
                                />
                              );
                            })}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              </div>
            </Panel>
          );
        })}
      </Collapse>
    </div>
  );
};
