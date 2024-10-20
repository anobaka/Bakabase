import { FolderOpenOutlined, SyncOutlined, UploadOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import OperationButton from '../OperationButton';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import { IwFsType } from '@/sdk/constants';
import { Button, Tooltip } from '@/components/bakaui';
import type { TreeEntryProps } from '@/pages/FileProcessor/TreeEntry';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import ExtractModal from '@/pages/FileProcessor/RootTreeEntry/components/ExtractModal';

type Props = {
  entry: Entry;
} & Pick<TreeEntryProps, 'capabilities'>;

export default (props: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { entry, capabilities } = props;

  const isExtractable = capabilities?.includes('extract') && entry.isDirectory && entry.childrenCount && entry.childrenCount > 0;

  return (
    <>
      <OperationButton
        isIconOnly
        onClick={(e) => {
          e.stopPropagation();
          BApi.tool.openFileOrDirectory({
            path: entry.path,
            openInDirectory: entry.type != IwFsType.Directory,
          });
        }}
      >
        <FolderOpenOutlined className={'text-base'} />
      </OperationButton>
      {isExtractable && (
        <Tooltip
          content={`(E)${t('Extract children to parent and delete current directory')}`}
          placement={'top'}
        >
          <Button
            className={'w-auto h-auto p-1 min-w-fit opacity-60 hover:opacity-100'}
            isIconOnly
            variant={'light'}
            onClick={(e) => {
              e.stopPropagation();
              createPortal(ExtractModal, {
                entries: [entry],
              });
            }}
          >
            <UploadOutlined className={'text-base'} />
          </Button>
        </Tooltip>
      )}
      {entry.isDirectoryOrDrive && (
        <OperationButton
          isIconOnly
          onClick={(e) => {
            entry.ref?.expand(true);
          }}
        >
          <SyncOutlined className={'text-base'} />
        </OperationButton>
      )}
    </>
  );
};
