import { Button, Collapse, Icon } from '@alifd/next';
import SimpleLabel from '@/components/SimpleLabel';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import { useTranslation } from 'react-i18next';
import type { BulkModificationFilterOperation } from '@/sdk/constants';
import { BulkModificationFilterGroupOperation, BulkModificationStatus, BulkModificationProperty } from '@/sdk/constants';
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
import { useUpdateEffect } from 'react-use';
import type {
  IMultiValueProcessorValue,
} from './components/ProcessDialog/Processors/MultiValueProcessor';


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

export default () => {
  const { t } = useTranslation();
  const [processing, setProcessing] = useState(false);

  const [bulkModifications, setBulkModifications] = useState<IBulkModification[]>(testBmsJson);

  const [demonstratorDataSources, setDemonstratorDataSources] = useState<{[property in BulkModificationProperty]?: Record<any, any>}>({});

  console.log('[BulkModifications]', bulkModifications, demonstratorDataSources);

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
    BApi.publisher.getAllPublishers().then(r => {
      const publishers = r.data || [];
      setDemonstratorDataSources(s => ({
        ...s,
        [BulkModificationProperty.Publisher]: publishers.reduce<Record<any, any>>((s, t) => {
          s[t.id!] = t.name!;
          return s;
        }, {}),
      }));
      console.log('set demonstrator data sources');
    });
  }, []);

  // useUpdateEffect(() => {
  // //   publishers
  //   const publisherIds = bulkModifications.reduce<number[]>((s, t) => {
  //     if (t.processes) {
  //       for (const p of t.processes) {
  //         if (p.property == BulkModificationProperty.Publisher) {
  //           const v: IMultiValueProcessorValue = p.value;
  //           if (v?.value) {
  //             v.value.forEach((id: number) => s.push(id));
  //           }
  //         }
  //       }
  //     }
  //     return s;
  //   }, []);
  //   BApi.publisher.getAllPublishers();
  // }, [bulkModifications]);

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
                          dataSources={demonstratorDataSources[p.property!]}
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
                    <div className="item">
                      <SimpleLabel status={'default'}>12323</SimpleLabel>
                      <Button text type={'primary'} className="path">
                        D:/123/456/788/adasdassad
                      </Button>
                      <div className="changes">
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                      </div>
                    </div>
                    <div className="item">
                      <SimpleLabel status={'default'}>12323</SimpleLabel>
                      <Button text type={'primary'} className="path">
                        D:/123/456/788/adasdassad
                      </Button>
                      <div className="changes">
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                      </div>
                    </div>
                    <div className="item">
                      <SimpleLabel status={'default'}>12323</SimpleLabel>
                      <Button text type={'primary'} className="path">
                        D:/123/456/788/adasdassad
                      </Button>
                      <div className="changes">
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                        <div className="change">
                          <div className="property">
                            <CustomIcon type={'segment'} size={'xs'} />
                            作者
                          </div>
                          <SimpleLabel status={'default'} className="current">123</SimpleLabel>
                          <Icon type="arrow-double-right" size={'small'} />
                          <SimpleLabel status={'default'} className="new">456</SimpleLabel>
                        </div>
                      </div>
                    </div>
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
