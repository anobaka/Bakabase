import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { IFileSystemSelectorProps } from '@/components/FileSystemSelector';
import FileSystemSelector from '@/components/FileSystemSelector';
import { createPortalOfComponent } from '@/components/utils';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';
import { Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';

interface IProps extends IFileSystemSelectorProps, DestroyableProps {
}

const FileSystemSelectorDialog = (props: IProps) => {
  const { t } = useTranslation();
  const {
    ...fsProps
  } = props;

  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  let title = 'Select file system entries';
  if (props.targetType != undefined) {
    switch (props.targetType) {
      case 'file':
        title = 'Select file';
        break;
      case 'folder':
        title = 'Select folder';
        break;
    }
  }

  return (
    <Modal
      size={'xl'}
      title={t(title)}
      visible={visible}
      footer={false}
      onDestroyed={props.onDestroyed}
      onClose={close}
      className={''}
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
    </Modal>
  );
};

FileSystemSelectorDialog.show = (props: IProps) => createPortalOfComponent(FileSystemSelectorDialog, props);

export default FileSystemSelectorDialog;
