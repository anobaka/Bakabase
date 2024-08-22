import { Dialog, Input, Loading, VirtualList } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AutoSizer, List } from 'react-virtualized';
import { AutoTextSize } from 'auto-text-size';
import { useUpdate, useUpdateEffect } from 'react-use';
import { createPortalOfComponent } from '@/components/utils';
import SimpleOneStepDialog, { ISimpleOneStepDialogProps } from '@/components/SimpleOneStepDialog';
import BApi from '@/sdk/BApi';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';
import SimpleLabel from '@/components/SimpleLabel';

interface IProps extends DialogProps {
  bmId: number;
}

const FilteredResourcesDialog = ({ bmId }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);

  const [resources, setResources] = useState<{ path: string }[]>([]);
  const [keyword, setKeyword] = useState<string>();
  const filteredResourcesRef = useRef<{ path: string }[]>([]);
  const updateFilteredResourceTimeoutRef = useRef<any>();

  const forceUpdate = useUpdate();

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  useEffect(() => {
    BApi.bulkModification.getBulkModificationFilteredResources(bmId).then(r => {
      const data = (r.data || []).map(r => ({ path: r.rawFullname! }));
      filteredResourcesRef.current = data;
      setResources(data);
    }).finally(() => {
      setLoading(false);
    });
  }, []);

  useUpdateEffect(() => {
    clearTimeout(updateFilteredResourceTimeoutRef.current);
    updateFilteredResourceTimeoutRef.current = setTimeout(() => {
      filteredResourcesRef.current = resources.filter(r => keyword == undefined || r.path.includes(keyword));
      forceUpdate();
    }, 500);
  }, [keyword]);

  const rowRenderer = ({ key, index, style }) => {
    return (
      <div key={key} style={style} className={'resource'}>
        <SimpleLabel status={'default'}>
          {index + 1}
        </SimpleLabel>
        <AutoTextSize maxFontSizePx={14} className={'path'}>
          {filteredResourcesRef.current[index].path}
        </AutoTextSize>
      </div>
    );
  };

  return (
    <Dialog
      className={'bulk-modification-filtered-resources-dialog'}
      visible={visible}
      onClose={close}
      onCancel={close}
      onOk={close}
      closeMode={['esc', 'mask', 'close']}
      footerActions={['ok']}
      v2
      width={'auto'}
      title={t('Filtered resources')}
    >
      <div className="panel">
        <Input
          onChange={v => setKeyword(v)}
          addonTextBefore={t('Search')}
          innerAfter={
            <CustomIcon
              type="search"
            // size="xs"
              style={{ margin: 4 }}
            />
        }
        />
      </div>
      <Loading visible={loading}>
        <div className="resources">
          <AutoSizer>
            {({ height, width }) => (
              <List
                height={height}
                rowCount={filteredResourcesRef.current.length}
                rowHeight={24}
                rowRenderer={rowRenderer}
                width={width}
              />
          )}
          </AutoSizer>
        </div>
      </Loading>
    </Dialog>
  );
};


FilteredResourcesDialog.show = (props: IProps) => createPortalOfComponent(FilteredResourcesDialog, props);

export default FilteredResourcesDialog;
