import { Dialog, Input, VirtualList } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AutoSizer, List } from 'react-virtualized';
import { AutoTextSize } from 'auto-text-size';
import { createPortalOfComponent } from '@/components/utils';
import SimpleOneStepDialog, { ISimpleOneStepDialogProps } from '@/components/SimpleOneStepDialog';
import BApi from '@/sdk/BApi';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';
import SimpleLabel from '@/components/SimpleLabel';

interface IProps extends DialogProps {
  resourceIds: number[];
}

const FilteredResourcesDialog = ({ resourceIds }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  const [resources, setResources] = useState<{ path: string }[]>([]);
  const virtualListRef = useRef<any>();

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  useEffect(() => {
    virtualListRef.current.recomputeRowHeights();
  }, [resources]);

  useEffect(() => {
    BApi.resource.getResourcesByKeys({ ids: resourceIds }).then(r => {
      setResources((r.data || []).map(r => ({ path: r.rawFullname! })));
    });
  }, []);

  const rowRenderer = ({ key, index, style }) => {
    return (
      <div key={key} style={style} className={'resource'}>
        <SimpleLabel status={'default'}>
          {index + 1}
        </SimpleLabel>
        <AutoTextSize maxFontSizePx={14} className={'path'}>
          {resources[index].path}
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
      <div className="resources">
        <AutoSizer>
          {({ height, width }) => (
            <List
              ref={virtualListRef}
              height={height}
              rowCount={resources.length}
              rowHeight={24}
              rowRenderer={rowRenderer}
              width={width}
            />
          )}
        </AutoSizer>
      </div>
    </Dialog>
  );
};


FilteredResourcesDialog.show = (props: IProps) => createPortalOfComponent(FilteredResourcesDialog, props);

export default FilteredResourcesDialog;
