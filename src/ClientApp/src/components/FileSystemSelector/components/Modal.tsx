import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { FileSystemSelectorProps } from '@/components/FileSystemSelector/models';
import { FileSystemSelectorPanel } from '@/components/FileSystemSelector';

interface IProps extends FileSystemSelectorProps, DestroyableProps {
}

export default (props: IProps) => {
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
      className={'h-full'}
    >
      <FileSystemSelectorPanel
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
