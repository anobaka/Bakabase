import { useState } from 'react';
import { Button, Dialog, Input, Table } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useTranslation } from 'react-i18next';
import ClickableIcon from '@/components/ClickableIcon';
import { createPortalOfComponent } from '@/components/utils';
import DeleteSameNameFilesDialog from '@/pages/FileProcessor/DeleteSameNameFilesDialog';
import BApi from '@/sdk/BApi';

interface IProps extends DialogProps {
  categoryId: number;
  onSubmitted: any;
}

const AddMediaLibraryInBulkDialog = ({
                                       categoryId,
                                       onSubmitted,
                                       ...dialogProps
                                     }: IProps) => {
  const { t } = useTranslation();
  const [nameAndPaths, setNameAndPaths] = useState<{ name: string; paths: string[] }[]>([]);
  const [visible, setVisible] = useState(true);

const close = () => {
  setVisible(false);
};

  return (
    <Dialog
      title={t('Add media libraries in bulk')}
      v2
      visible={visible}
      width={'auto'}
      style={{ minWidth: 600 }}
      onOk={async () => {
        const model = { nameAndPaths: nameAndPaths.reduce<Record<string, string[]>>((s, t) => {
            s[t.name] = t.paths;
            return s;
          }, {}) };
        const rsp = await BApi.mediaLibrary.addMediaLibrariesInBulk(categoryId, model);
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
        dataSource={nameAndPaths}
      >
        <Table.Column
          title={t('Media libraries')}
          dataIndex={'name'}
          cell={(name, i, r) => {
            return (
              <Input
                placeholder={t('Name')}
                trim
                hasClear
                onChange={v => {
                  nameAndPaths[i].name = v;
                }}
              />
            );
          }}
        />
        <Table.Column
          title={t('Root paths')}
          dataIndex={'paths'}
          cell={(paths, i, r) => {
            const elements = (paths || []).map((p, j) => {
              return (
                <Input
                  style={{ width: 800 }}
                  placeholder={t('Root path')}
                  trim
                  key={j}
                  onChange={vp => {
                    paths[j] = vp;
                  }}
                  hasClear
                  addonAfter={(
                    <ClickableIcon
                      style={{ marginLeft: 5 }}
                      colorType={'danger'}
                      type={'delete'}
                      onClick={() => {
                        paths.splice(j, 1);
                        setNameAndPaths([...nameAndPaths]);
                      }}
                    />
                  )}
                />
              );
            });
            elements.push(
              <Button
                text
                // size={'small'}
                key={-1}
                type={'primary'}
                onClick={() => {
                  if (!paths) {
                    paths = [];
                  }
                  paths.push('');
                  setNameAndPaths([...nameAndPaths]);
                }}
              >
                {t('Add root path')}
              </Button>,
            );
            return (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
                {elements}
              </div>
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
          setNameAndPaths([...nameAndPaths, { name: '', paths: [] }]);
        }}
      >
        {t('Add a media library')}
      </Button>
    </Dialog>
  );
};

AddMediaLibraryInBulkDialog.show = (props: IProps) => createPortalOfComponent(AddMediaLibraryInBulkDialog, props);

export default AddMediaLibraryInBulkDialog;
