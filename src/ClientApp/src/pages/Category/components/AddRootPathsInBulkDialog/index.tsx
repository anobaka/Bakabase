import { useState } from 'react';
import { Button, Dialog, Input, Table } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useTranslation } from 'react-i18next';
import ClickableIcon from '@/components/ClickableIcon';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';

interface IProps extends DialogProps {
  libraryId: number;
  onSubmitted: any;
}

const AddRootPathsInBulkDialog = ({
                                       libraryId,
                                       onSubmitted,
                                       ...dialogProps
                                     }: IProps) => {
  const { t } = useTranslation();
  const [paths, setPaths] = useState<string[]>([]);
  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  return (
    <Dialog
      title={t('Add root paths in bulk')}
      v2
      visible={visible}
      width={'auto'}
      style={{ minWidth: 600 }}
      onOk={async () => {
        const rsp = await BApi.mediaLibrary.addMediaLibraryRootPathsInBulk(libraryId, { rootPaths: paths });
        if (!rsp.code) {
          onSubmitted?.();
          close();
        }
      }}
      onClose={close}
      onCancel={close}
      {...dialogProps}
    >
      <Table
        hasBorder={false}
        dataSource={paths}
      >
        <Table.Column
          title={t('Root paths')}
          dataIndex={'name'}
          cell={(name, i, r) => {
            return (
              <Input
                style={{ width: 800 }}
                placeholder={t('Root path')}
                trim
                hasClear
                onChange={v => {
                  paths[i] = v;
                }}
                addonAfter={(
                  <ClickableIcon
                    style={{ marginLeft: 5 }}
                    colorType={'danger'}
                    type={'delete'}
                    onClick={() => {
                      paths.splice(i, 1);
                      setPaths([...paths]);
                    }}
                  />
                )}
              />
            );
          }}
        />
      </Table>
      <Button
        style={{ marginTop: 5 }}
        text
        // size={'small'}
        type={'primary'}
        onClick={() => {
          setPaths([...paths, '']);
        }}
      >
        {t('Add a root path')}
      </Button>
    </Dialog>
  );
};

AddRootPathsInBulkDialog.show = (props: IProps) => createPortalOfComponent(AddRootPathsInBulkDialog, props);

export default AddRootPathsInBulkDialog;
