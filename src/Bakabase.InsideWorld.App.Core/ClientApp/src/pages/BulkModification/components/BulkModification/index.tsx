import { Button, Collapse, Dialog, Loading, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useTour } from '@reactour/tour';
import SimpleLabel from '@/components/SimpleLabel';
import type { BulkModificationProperty, BulkModificationStatus } from '@/sdk/constants';
import { BulkModificationFilterGroupOperation, type BulkModificationFilterOperation } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { IResourceDiff } from '@/pages/BulkModification/components/BulkModification/ResourceDiff';
import ResourceDiff from '@/pages/BulkModification/components/BulkModification/ResourceDiff';
import type { IVariable } from '@/pages/BulkModification/components/BulkModification/Variables';
import Variables from '@/pages/BulkModification/components/BulkModification/Variables';
import FilterGroup from '@/pages/BulkModification/components/BulkModification/FilterGroup';
import ProcessDemonstrator from '@/pages/BulkModification/components/BulkModification/ProcessDemonstrator';
import ProcessDialog from '@/pages/BulkModification/components/BulkModification/ProcessDialog';
import FilteredResourcesDialog from '@/pages/BulkModification/components/BulkModification/FilteredResourcesDialog';
import ClickableIcon from '@/components/ClickableIcon';


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

export interface IBulkModification {
  id: number;
  name: string;
  status: BulkModificationStatus;
  createdAt: string;
  variables?: IVariable[];
  filter?: IBulkModificationFilterGroup;
  processes?: IBulkModificationProcess[];
  filteredResourceIds?: number[];
}

interface IResourceModificationResult {
  id: number;
  path: string;
  diffs: IResourceDiff[];
}

interface IProps {
  bm: IBulkModification;
  displayDataSources: { [property in BulkModificationProperty]?: Record<any, any> };
  onChange: (bm: IBulkModification, save: boolean) => Promise<{ code: number; message?: string }>;
}

export default ({
                  bm: propsBm,
                  displayDataSources,
                  onChange,
                }: IProps) => {
  const { t } = useTranslation();
  const [processing, setProcessing] = useState(false);
  const [results, setResults] = useState<IResourceModificationResult[]>();
  const [loadingDiffs, setLoadingDiffs] = useState(false);
  const [bm, setBm] = useState(propsBm);

  const saveChanges = (changes: Partial<IBulkModification>, save: boolean) => {
    const newBm = { ...bm, ...changes };
    onChange(newBm, save).then(r => {
      if (!r.code) {
        setBm(newBm);
      }
    });
  };

  const {
    isOpen,
    currentStep,
    steps,
    setIsOpen,
    setCurrentStep,
    setSteps,
  } = useTour();

  const loadPrevDiffs = async () => {
    BApi.bulkModification.getBulkModificationResourceDiffs(bm.id).then(r => {
      if (!r.code) {
        const diffs = r.data || [];
        const resultMap: Record<number, IResourceModificationResult> = [];
        for (const d of diffs) {
          let r = resultMap[d.resourceId!];
          if (!r) {
            r = resultMap[d.resourceId!] = {
              id: d.resourceId!,
              path: d.resourcePath!,
              diffs: [],
            };
          }
          // @ts-ignore
          r.diffs.push(d);
        }
        setResults(Object.values(resultMap));
      }
    });
  };

  return (
    <>
      <div className="filters-panel">
        <div className="title">
          {t('Filters')}
        </div>
        <div className="content">
          <div className="filters">
            <FilterGroup
              onChange={v => {
                saveChanges({ filter: v }, true);
              }}
              group={bm.filter || { operation: BulkModificationFilterGroupOperation.And }}
              isRoot
            />
          </div>
          <div className="opts">
            <Button
              type={'primary'}
              size={'small'}
              loading={processing}
              onClick={() => {
                setProcessing(true);
                BApi.bulkModification.performBulkModificationFiltering(bm.id).then(r => {
                  saveChanges({
                    filteredResourceIds: r.data!,
                  }, false);
                }).finally(() => {
                  setProcessing(false);
                });
              }}
            >筛选</Button>
            <div className={'resource-count'}>
              总计筛选出<span>{bm.filteredResourceIds?.length ?? 0}</span>个资源
            </div>
            <Button
              type={'primary'}
              text
              size={'small'}
              onClick={() => {
                FilteredResourcesDialog.show({
                  bmId: bm.id!,
                });
              }}
            >查看完整筛选结果</Button>
          </div>
        </div>
      </div>
      <div className="variables-panel">
        <div className="title">
          {t('Variables')}
        </div>
        <Variables
          variables={bm.variables}
          onChange={v => {
            saveChanges({ variables: v }, true);
          }}
        />
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
                  key={j}
                  dataSources={displayDataSources[p.property!]}
                  process={p}
                  index={j}
                  variables={bm.variables}
                  onChange={p => {
                    const newProcesses = [...bm.processes!];
                    newProcesses[j] = p;
                    saveChanges({ processes: newProcesses }, true);
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
                    saveChanges({
                      processes: [
                        ...(bm.processes || []),
                        pv,
                      ],
                    }, true);
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
            <Button
              type={'secondary'}
              size={'small'}
              loading={loadingDiffs}
              onClick={() => {
                const dialog = Dialog.show({
                  title: t('Processing'),
                  // content: t('Processing'),
                  closeable: false,
                  footer: false,
                });
                BApi.bulkModification.calculateBulkModificationResourceDiffs(bm.id).then(r => {
                  if (!r.code) {
                    loadPrevDiffs();
                  }
                }).finally(() => {
                  dialog.hide();
                });
              }}
            >{t('Calculate resource diffs')}</Button>
            <Button
              type={'normal'}
              size={'small'}
              onClick={() => {
                loadPrevDiffs();
              }}
            >{t('Check previous result')}</Button>
            <Button
              type={'primary'}
              size={'small'}
              onClick={() => {
                Dialog.confirm({
                  title: t('Apply bulk modification'),
                  content: t('All changes will be applied to resources, and there is no way back. Are you sure to apply the bulk modification?'),
                  onOk: () => {
                    const dialog = Dialog.show({
                      title: t('Processing'),
                      // content: t('Processing'),
                      closeable: false,
                      footer: false,
                    });
                    setTimeout(() => {
                      dialog.hide();
                    }, 3000);
                    // BApi.bulkModification(bm.id).then(r => {
                    //   if (!r.code) {
                    //     Message.success(t('Bulk modification applied successfully'));
                    //   } else {
                    //     Message.error(r.message);
                    //   }
                    // }).finally(() => {
                    //   setProcessing(false);
                    // });
                  },
                });
              }}
            >{t('Apply')}</Button>
          </div>
          <div className="preview">
            {results?.map(r => {
              return (
                <div className="item">
                  <div className="resource">
                    <SimpleLabel status={'default'} className={'id'}>{r.id}</SimpleLabel>
                    <div className="path">
                      {r.path}
                    </div>
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
    </>
  );
};
