import { useCallback, useEffect } from 'react';
import { Dialog, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import MediaLibraryPathSelector from '@/components/MediaLibraryPathSelector';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';


interface Props {
  paths: string[];
}

function FileEntryMover({ paths = [] }: Props) {
  const { t } = useTranslation();

  useEffect(() => {

  }, []);

  const onSelect = useCallback(path => {
    if (path) {
      const dialog = Dialog.show({
        title: t('Moving'),
        footer: false,
        closeMode: ['close'],
        visible: true,
        hasMask: true,
        v2: true,
      });
      return BApi.file.moveEntries({
        destDir: path,
        entryPaths: paths,
      })
        .then(() => {
          Message.success(t('Success'));
        })
        .finally(() => {
          dialog.hide();
        });
    }
    return;
  }, []);

  return (
    <MediaLibraryPathSelector onSelect={onSelect} />
  );
}


FileEntryMover.show = (props: Props) => createPortalOfComponent(FileEntryMover, props);

export default FileEntryMover;
