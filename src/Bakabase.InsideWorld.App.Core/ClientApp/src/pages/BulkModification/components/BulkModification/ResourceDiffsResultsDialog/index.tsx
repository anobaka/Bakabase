import { Button, Dialog, Input, Loading, Pagination } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate, useUpdateEffect } from 'react-use';
import ResourceDiff, { type IResourceDiff } from '../ResourceDiff';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';
import SimpleLabel from '@/components/SimpleLabel';
import type { BulkModificationProperty } from '@/sdk/constants';

interface IProps extends DialogProps {
  bmId: number;
  displayDataSources: { [property in BulkModificationProperty]?: Record<any, any> };
}

interface IResourceModificationResult {
  id: number;
  path: string;
  diffs: IResourceDiff[];
}

const PageSize = 20;

const ResourceDiffsResultsDialog = ({
                                      bmId,
                                      displayDataSources,
                                    }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);

  const [results, setResults] = useState<IResourceModificationResult[]>([]);
  const [form, setForm] = useState<{ path?: string; pageIndex: number }>({
    path: undefined,
    pageIndex: 0,
  });
  const [filteredResults, setFilteredResults] = useState<IResourceModificationResult[]>([]);
  const [currentPageResults, setCurrentPageResults] = useState<IResourceModificationResult[]>([]);
  const forceUpdate = useUpdate();
  const updateFilteredResultsTimeoutRef = useRef<any>();

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  useEffect(() => {
    BApi.bulkModification.getBulkModificationResourceDiffs(bmId).then(r => {
      const data = (r.data || []);
      // @ts-ignore
      setResults(data);
    }).finally(() => {
      setLoading(false);
    });
  }, []);

  useUpdateEffect(() => {
    clearTimeout(updateFilteredResultsTimeoutRef.current);
    updateFilteredResultsTimeoutRef.current = setTimeout(() => {
      const frs = results.filter(r => form.path == undefined || form.path.length == 0 || r.path.includes(form.path));
      const cprs = frs.slice(form.pageIndex * PageSize, (form.pageIndex + 1) * PageSize);
      setFilteredResults(frs);
      setCurrentPageResults(cprs);
      forceUpdate();
    }, 500);
  }, [form, results]);

  return (
    <Dialog
      className={'bulk-modification-resource-diffs-dialog'}
      visible={visible}
      onClose={close}
      onCancel={close}
      onOk={close}
      closeMode={['esc', 'mask', 'close']}
      footerActions={['ok']}
      v2
      width={'auto'}
      title={t('Resource diffs')}
    >
      <div className="panel">
        <div className="left">
          <Input
            size={'small'}
            onChange={v => setForm({
              ...form,
              path: v,
              pageIndex: 0,
            })}
            addonTextBefore={t('Search')}
            innerAfter={
              <CustomIcon
                type="search"
                size="small"
                style={{ margin: 4 }}
              />
            }
          />
          {/* <Button type={'primary'} size={'small'}>{t('Search')}</Button> */}
        </div>
        <div className="right">
          <Pagination
            size="small"
            showJump={false}
            pageSize={PageSize}
            total={filteredResults.length}
            onChange={p => setForm({ pageIndex: p - 1 })}
          />
        </div>
      </div>
      <Loading visible={loading}>
        <div className="results">
          {currentPageResults?.map(r => {
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
      </Loading>
    </Dialog>
  );
};


ResourceDiffsResultsDialog.show = (props: IProps) => createPortalOfComponent(ResourceDiffsResultsDialog, props);

export default ResourceDiffsResultsDialog;
