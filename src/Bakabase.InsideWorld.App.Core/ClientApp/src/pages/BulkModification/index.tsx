import { Button, Collapse, Icon } from '@alifd/next';
import SimpleLabel from '@/components/SimpleLabel';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import { useTranslation } from 'react-i18next';
import type {
  BulkModificationFilterOperation,
  BulkModificationProcessOperation,
  BulkModificationProperty,
} from '@/sdk/constants';
import { BulkModificationFilterGroupOperation, BulkModificationStatus } from '@/sdk/constants';
import { useState } from 'react';
import FilterGroup from '@/pages/BulkModification/components/FilterGroup';
import BApi from '@/sdk/BApi';
import ProcessDialog from '@/pages/BulkModification/components/ProcessDialog';

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
  operation?: BulkModificationProcessOperation;
  regexEnabled?: boolean;
  find?: string;
  replace?: string;
}

interface IBulkModification {
  id: number;
  name: string;
  status: BulkModificationStatus;
  createdAt: string;
  filter?: IBulkModificationFilterGroup;
  modifications?: IBulkModificationProcess[];
}

export default () => {
  const { t } = useTranslation();
  const [processing, setProcessing] = useState(false);

  const [bulkModifications, setBulkModifications] = useState<IBulkModification[]>([{
    id: 5,
    status: BulkModificationStatus.Initial,
    createdAt: '2023-12-12 00:00:05',
    name: '批量修改作者',
  }]);

  const [expandedKeys, setExpandedKeys] = useState<string[]>(['5']);

  return (
    <div className={'bulk-modification-page'}>
      <div className="header">
        <div className="title">Bulk Modification</div>
        <Button type={'primary'} size={'small'}>{t('Create a bulk modification')}</Button>
      </div>
      <Collapse className={'bulk-modifications'} expandedKeys={expandedKeys} onExpand={keys => setExpandedKeys(keys)}>
        {bulkModifications.map(bm => {
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
                    <Button type={'normal'} size={'small'}>{t('Create from this')}</Button>
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
                  Filters
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
              <div className="processes-panel">
                <div className="title">
                  Processes
                </div>
                <div className="content">
                  <div className="processes">
                    <div className="process">
                      <div className="no">
                        {/* 1 */}
                        <SimpleLabel status={'default'}>1</SimpleLabel>
                      </div>
                      <div className="property">
                        <CustomIcon type={'segment'} size={'small'} />
                        名称
                      </div>
                      <div className="operation replace">
                        <CustomIcon type={'edit-square'} size={'small'} />
                        修改
                      </div>
                      <div className="value">
                        xxxxxxxxx
                      </div>
                    </div>
                    <div className="process">
                      <div className="no">
                        {/* 2 */}
                        <SimpleLabel status={'default'}>2</SimpleLabel>
                      </div>
                      <div className="property">
                        {/* <SimpleLabel status={'default'}>名称</SimpleLabel> */}
                        <CustomIcon type={'segment'} size={'small'} />
                        名称
                      </div>
                      <div className="operation merge">
                        <CustomIcon type={'git-merge-line'} size={'small'} />
                        合并
                      </div>
                      <div className="value">
                        xxxxxxxxx
                      </div>
                    </div>
                  </div>
                  <div className="opts">
                    <Button
                      type={'normal'}
                      size={'small'}
                      onClick={() => {
                      ProcessDialog.show({

                      });
                    }}
                    >添加修改步骤</Button>
                  </div>
                </div>
              </div>
              <div className="result-panel">
                <div className="title">
                  Result
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
