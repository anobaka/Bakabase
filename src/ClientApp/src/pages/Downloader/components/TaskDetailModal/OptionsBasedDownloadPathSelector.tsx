import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { Button } from '@/components/bakaui';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Options = {
  downloader?: {
    defaultPath?: string;
  };
  initialized: boolean;
};

type Props = {
  options: Options;
  downloadPath?: string;
  onChange?: (downloadPath?: string) => void;
};

export default ({
                  options,
                  downloadPath: propsDownloadPath,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const downloadPath = propsDownloadPath ?? options.downloader?.defaultPath;

  useEffect(() => {
    if (options.initialized && !propsDownloadPath && options.downloader?.defaultPath) {
      onChange?.(options.downloader.defaultPath);
    }
  }, [options.initialized]);

  return (
    <>
      <div>{t('Download path')}</div>
      <div>
        <Button
          size={'sm'}
          color={'primary'}
          variant={'light'}
          onClick={() => {
             createPortal(FileSystemSelectorDialog, {
               onSelected: e => {
                 onChange?.(e.path);
               },
               targetType: 'folder',
               startPath: downloadPath,
               defaultSelectedPath: downloadPath,
             });
           }}
        >
          {downloadPath ?? t('Select download path')}
        </Button>
      </div>
    </>
  );
};
