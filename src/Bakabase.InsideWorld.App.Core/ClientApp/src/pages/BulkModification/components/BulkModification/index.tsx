import { Balloon, Button, Collapse, Dialog, Icon, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import moment from 'moment';
import { useUpdateEffect } from 'react-use';
import type {
  BulkModificationProperty } from '@/sdk/constants';
import {
  BulkModificationFilterGroupOperation,
  type BulkModificationFilterOperation,
  BulkModificationStatus,
} from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type { IVariable } from '@/pages/BulkModification/components/BulkModification/Variables';
import Variables from '@/pages/BulkModification/components/BulkModification/Variables';
import FilterGroup from '@/pages/BulkModification/components/BulkModification/FilterGroup';
import ProcessDemonstrator from '@/pages/BulkModification/components/BulkModification/ProcessDemonstrator';
import ProcessDialog from '@/pages/BulkModification/components/BulkModification/ProcessDialog';
import FilteredResourcesDialog from '@/pages/BulkModification/components/BulkModification/FilteredResourcesDialog';
import ResourceDiffsResultsDialog
  from '@/pages/BulkModification/components/BulkModification/ResourceDiffsResultsDialog';
import { convertFromApiModel, convertToApiModel } from '@/pages/BulkModification/helpers';


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
  calculatedAt?: string;
  filteredAt?: string;
  appliedAt?: string;
  revertedAt?: string;
  variables?: IVariable[];
  filter?: IBulkModificationFilterGroup;
  processes?: IBulkModificationProcess[];
  filteredResourceIds?: number[];
}

interface IProps {
  bm: IBulkModification;
  displayDataSources: { [property in BulkModificationProperty]?: Record<any, any> };
  onChange: (bm: IBulkModification) => any;
}

export default ({
                  bm: propsBm,
                  displayDataSources,
                  onChange,
                }: IProps) => {
  const { t } = useTranslation();
  const [processing, setProcessing] = useState(false);
  const [bm, setBm] = useState(propsBm);

  useUpdateEffect(() => {
    onChange(bm);
  }, [bm]);

  useUpdateEffect(() => {
    if (propsBm != bm) {
      setBm(propsBm);
    }
  }, [propsBm]);

  const refresh = async () => {
    const rsp = await BApi.bulkModification.getBulkModificationById(bm.id);
    if (rsp.data) {
      setBm(convertFromApiModel(rsp.data));
    }
  };

  const saveChanges = async (changes: Partial<IBulkModification>) => {
    await BApi.bulkModification.putBulkModification(bm.id, convertToApiModel({ ...bm, ...changes }));
    await refresh();
  };

  useEffect(() => {

  }, []);

  const renderResultInfo = () => {
    const data: any[] = [];
    if (bm.calculatedAt) {
      data.push(t('Calculated at {{datetime}}', { datetime: bm.calculatedAt }));
    }
    if (bm.appliedAt) {
      data.push(t('Applied at {{datetime}}', { datetime: bm.appliedAt }));
    }
    if (bm.revertedAt) {
      data.push(t('Reverted at {{datetime}}', { datetime: bm.revertedAt }));
    }
    if (data.length > 0) {
      return (
        <div className={'info'}>
          {data.map((d, i) => {
            return (
              <div
                key={i}
              >{d}</div>
            );
          })}
        </div>
      );
    }
    return;
  };

  const calculateResourceDiffs = async () => {
    const ac = new AbortController();
    const ct = ac.signal;
    const dialog = Dialog.show({
      title: (
        <div>
          {t('Processing')}
          &nbsp;
          <Icon type={'loading'} />
        </div>
      ),
      v2: true,
      width: 300,
      closeMode: [],
      footerActions: ['cancel'],
      cancelProps: {
        children: t('Abort'),
        warning: true,
        type: 'primary',
      },
      onCancel: () => {
        ac.abort();
      },
    });
    try {
      const r = await BApi.bulkModification.calculateBulkModificationResourceDiffs(bm.id, { signal: ct });
      if (!r.code) {
        await refresh();
        ResourceDiffsResultsDialog.show({
          bmId: bm.id,
          displayDataSources,
        });
      }
    } finally {
      dialog.hide();
    }
  };

  const alertIfRevertingWillBeDisabled = (callback?: any) => {
    if (bm.appliedAt) {
      const appliedAt = moment(bm.appliedAt!);
      const datetimes = [bm.filteredAt!, bm.calculatedAt!].map(d => moment(d));
      const revertingWillBeDisabled = datetimes.every(d => d.isBefore(appliedAt));
      console.log(bm.appliedAt, datetimes, revertingWillBeDisabled, appliedAt.isBefore(datetimes[0]));
      if (revertingWillBeDisabled) {
        Dialog.confirm({
          title: t('Reverting will be disabled'),
          content: t('Continuing will result in the inability to revert the applied changes. Do you want to proceed?'),
          v2: true,
          width: 'auto',
          closeable: false,
          onOk: () => {
            callback && callback();
          },
        });
        return;
      }
    }
    callback && callback();
  };

  const editable = bm.status != BulkModificationStatus.Closed;

  return (
    <>
      <div className="filters-panel">
        <div className="title">
          {t('Filters')}
        </div>
        <div className="content">
          <div className="filters">
            <FilterGroup
              onChange={async v => {
                await saveChanges({ filter: v });
              }}
              group={bm.filter || { operation: BulkModificationFilterGroupOperation.And }}
              isRoot
              editable={editable}
            />
          </div>
          <div className="opts">
            {editable && (
              <Button
                type={'primary'}
                size={'small'}
                loading={processing}
                onClick={async () => {
                  alertIfRevertingWillBeDisabled(async () => {
                    setProcessing(true);
                    try {
                      await BApi.bulkModification.performBulkModificationFiltering(bm.id);
                      await refresh();
                    } finally {
                      setProcessing(false);
                    }
                  });
                }}
              >{t('Filter(Verb)')}</Button>
            )}
            <div className={'resource-count'}>
              {t('{{count}} resources have been filtered out', { count: bm.filteredResourceIds?.length || 0 })}
            </div>
            {bm.filteredAt && (
              <div className={'filtered-at'}>{t('Filtered at {{datetime}}', { datetime: bm.filteredAt })}</div>
            )}
            {bm.filteredResourceIds && bm.filteredResourceIds.length > 0 && (
              <Button
                type={'primary'}
                text
                size={'small'}
                onClick={() => {
                  FilteredResourcesDialog.show({
                    bmId: bm.id!,
                  });
                }}
              >{t('Check all the resources that have been filtered out')}</Button>
            )}
          </div>
        </div>
      </div>
      <div className="variables-panel">
        <div className="title">
          {t('Variables')}
        </div>
        <Variables
          variables={bm.variables}
          onChange={async v => {
            await saveChanges({ variables: v });
          }}
          editable={editable}
        />
      </div>
      <div className="processes-panel">
        <div className="title">
          {t('Processes')}
        </div>
        <div className="content">
          {bm.processes && bm.processes.length > 0 && (
            <div className="processes">
              {bm.processes.map((p, j) => {
                return (
                  <ProcessDemonstrator
                    editable={editable}
                    key={j}
                    dataSources={displayDataSources[p.property!]}
                    process={p}
                    index={j}
                    variables={bm.variables}
                    onChange={async p => {
                      const newProcesses = [...bm.processes!];
                      newProcesses[j] = p;
                      await saveChanges({ processes: newProcesses });
                    }}
                    onRemove={async () => {
                      const newProcesses = [...bm.processes!];
                      newProcesses.splice(j, 1);
                      await saveChanges({ processes: newProcesses });
                    }}
                  />
                );
              })}
            </div>
          )}
          {editable && (
            <div className="opts">
              <Button
                type={'normal'}
                size={'small'}
                onClick={() => {
                  ProcessDialog.show({
                    variables: bm.variables,
                    onSubmit: async pv => {
                      await saveChanges({
                        processes: [
                          ...(bm.processes || []),
                          pv,
                        ],
                      });
                    },
                  });
                }}
              >添加修改步骤</Button>
            </div>
          )}
        </div>
      </div>
      <div className="result-panel">
        <div className="title">
          {t('Result')}
        </div>
        <div className="content">
          {renderResultInfo()}
          <div className="opts">
            {bm.filteredAt && editable && (
              <Balloon.Tooltip
                trigger={(
                  <Button
                    type={'secondary'}
                    size={'small'}
                    onClick={() => {
                      alertIfRevertingWillBeDisabled(async () => {
                        await calculateResourceDiffs();
                      });
                    }}
                  >{t(bm.calculatedAt ? 'Recalculate resource diffs' : 'Calculate resource diffs')}</Button>
                )}
                v2
                align={'t'}
              >
                {t('This process may take a long time, please be patient')}
              </Balloon.Tooltip>
            )}
            {bm.calculatedAt && (
              <Button
                disabled={!bm.calculatedAt}
                type={'normal'}
                size={'small'}
                onClick={() => {
                  ResourceDiffsResultsDialog.show({
                    bmId: bm.id,
                    displayDataSources,
                  });
                }}
              >{t('Check previous calculation result')}</Button>
            )}
            {bm.calculatedAt && editable && (
              <Button
                type={'primary'}
                size={'small'}
                onClick={() => {
                  Dialog.confirm({
                    title: t('Apply bulk modification'),
                    content: (
                      <div>
                        {t('All changes will be applied to resources, and there is no way back. Are you sure to apply the bulk modification?')}
                      </div>
                    ),
                    onOk: () => {
                      const dialog = Dialog.show({
                        title: t('Processing'),
                        closeable: false,
                        footer: false,
                        v2: true,
                        width: 'auto',
                      });
                      BApi.bulkModification.applyBulkModification(bm.id).then(r => {
                        if (!r.code) {
                          Message.success(t('Bulk modification has been applied successfully'));
                          refresh();
                        }
                      }).finally(() => {
                        dialog.hide();
                      });
                    },
                  });
                }}
              >{t('Apply')}</Button>
            )}
            {bm.appliedAt && editable && (
              <Button
                type={'normal'}
                warning
                size={'small'}
                onClick={() => {
                  Dialog.confirm({
                    title: t('Revert bulk modification'),
                    content: t('All changes will be reverted, and there is no way back. Are you sure to revert current bulk modification?'),
                    onOk: () => {
                      const dialog = Dialog.show({
                        title: t('Processing'),
                        closeable: false,
                        footer: false,
                        v2: true,
                        width: 'auto',
                      });
                      BApi.bulkModification.revertBulkModification(bm.id).then(r => {
                        if (!r.code) {
                          Message.success(t('Bulk modification has been reverted successfully'));
                          refresh();
                        }
                      }).finally(() => {
                        dialog.hide();
                      });
                    },
                  });
                }}
              >{t('Revert')}</Button>
            )}
          </div>
        </div>
      </div>
    </>
  );
};
