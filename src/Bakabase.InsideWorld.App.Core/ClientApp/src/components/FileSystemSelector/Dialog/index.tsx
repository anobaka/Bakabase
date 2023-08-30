import { Dialog } from '@alifd/next';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { IFileSystemSelectorProps } from '@/components/FileSystemSelector';
import FileSystemSelector from '@/components/FileSystemSelector';
import { createPortalOfComponent } from '@/components/utils';

interface IProps extends IFileSystemSelectorProps {
  afterClose?: () => any;
}

const FileSystemSelectorDialog = (props: IProps) => {
  const { t } = useTranslation();
  const {
    afterClose,
    ...fsProps
  } = props;

  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  return (
    <Dialog
      v2
      width={700}
      closeMode={['close', 'esc', 'mask']}
      title={t('Select file system entry')}
      visible={visible}
      footer={false}
      closeIcon={false}
      afterClose={afterClose}
      onCancel={close}
      onClose={close}
    >
      <FileSystemSelector
        {...fsProps}
        onSelected={e => {
          close();
          if (fsProps.onSelected) {
            fsProps.onSelected(e);
          }
        }}
        onCancel={() => {
          close();
          if (fsProps.onCancel) {
            fsProps.onCancel();
          }
        }}
      />
    </Dialog>
  );
};

FileSystemSelectorDialog.show = (props: IProps) => createPortalOfComponent(FileSystemSelectorDialog, props);

export default FileSystemSelectorDialog;
