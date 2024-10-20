import { MenuItem, useMenuState } from '@szhsin/react-menu';
import React, { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import {
  CopyOutlined,
  DeleteColumnOutlined,
  DeleteOutlined, GroupOutlined,
  MergeOutlined,
  SendOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Dialog, Message } from '@alifd/next';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import { IwFsEntryAction } from '@/core/models/FileExplorer/Entry';
import type { TreeEntryProps } from '@/pages/FileProcessor/TreeEntry';
import BApi from '@/sdk/BApi';
import { IwFsType } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import ExtractModal from '@/pages/FileProcessor/RootTreeEntry/components/ExtractModal';
import WrapModal from '@/pages/FileProcessor/RootTreeEntry/components/WrapModal';
import DeleteConfirmationModal from '@/pages/FileProcessor/RootTreeEntry/components/DeleteConfirmationModal';
import MediaLibraryPathSelectorV2 from '@/components/MediaLibraryPathSelectorV2';
import DeleteItemsWithSameNamesModal
  from '@/pages/FileProcessor/RootTreeEntry/components/DeleteItemsWithSameNamesModal';
import GroupModal from '@/pages/FileProcessor/RootTreeEntry/components/GroupModal';

type Props = {
  selectedEntries: Entry[];
  contextMenuEntry?: Entry;
} & Pick<TreeEntryProps, 'capabilities'>;

type Item = {
  icon: any;
  label: string;
  onClick: () => any;
};

export default ({
                  selectedEntries,
                  capabilities,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const items: Item[] = [];

  if (selectedEntries.length > 0) {
    const decompressableEntries = selectedEntries.filter(x => x.actions.includes(IwFsEntryAction.Decompress));
    const directoryEntries = selectedEntries.filter(e => e.type == IwFsType.Directory);

    if (capabilities?.includes('decompress') && decompressableEntries.length > 0) {
      items.push({
        icon: <CopyOutlined className={'text-base'} />,
        label: t('Decompress {{count}} files', { count: decompressableEntries.length }),
        onClick: () => {
          BApi.file.decompressFiles({ paths: decompressableEntries.map(e => e.path) });
        },
      });
    }

    if (capabilities?.includes('extract') && directoryEntries.length > 0) {
      items.push({
        icon: <UploadOutlined className={'text-base'} />,
        label: t('Extract {{count}} directories', { count: directoryEntries.length }),
        onClick: () => {
          createPortal(ExtractModal, { entries: selectedEntries });
        },
      });
    }

    if (capabilities?.includes('wrap')) {
      items.push({
        icon: <MergeOutlined className={'text-base'} />,
        label: t('Wrap {{count}} items', { count: selectedEntries.length }),
        onClick: () => {
          createPortal(WrapModal, { entries: selectedEntries });
        },
      });
    }

    if (capabilities?.includes('delete')) {
      items.push({
        icon: <DeleteOutlined className={'text-base'} />,
        label: t('Delete {{count}} items', { count: selectedEntries.length }),
        onClick: () => {
          createPortal(DeleteConfirmationModal, { paths: selectedEntries.map(e => e.path) });
        },
      });
    }

    if (capabilities?.includes('delete-all-by-name')) {
      items.push({
        icon: <DeleteColumnOutlined className={'text-base'} />,
        label: t('Delete items with the same names'),
        onClick: () => {
          createPortal(DeleteItemsWithSameNamesModal, {
            entries: selectedEntries,
            workingDirectory: selectedEntries[0].root.path,
          });
        },
      });
    }

    if (capabilities?.includes('move')) {
      items.push({
        icon: <SendOutlined className={'text-base'} />,
        label: t('Move {{count}} items', { count: selectedEntries.length }),
        onClick: () => {
          createPortal(MediaLibraryPathSelectorV2, {
            onSelect: (id, path) => {
              return BApi.file.moveEntries({
                destDir: path,
                entryPaths: selectedEntries.map(e => e.path),
              });
            },
          });
        },
      });
    }

    if (capabilities?.includes('group')) {
      const targetEntries = selectedEntries.filter(x => !x.isDirectory);
      if (targetEntries.length > 1) {
        items.push({
          icon: <GroupOutlined className={'text-base'} />,
          label: t('Group {{count}} items', { count: targetEntries.length }),
          onClick: () => {
            createPortal(GroupModal, {
              entries: selectedEntries,
              groupInternal: false,
            });
          },
        });
      }

      if (selectedEntries.length == 1 && selectedEntries[0].isDirectory) {
        items.push({
          icon: <GroupOutlined className={'text-base'} />,
          label: t('Group internal items'),
          onClick: () => {
            createPortal(GroupModal, {
              entries: selectedEntries,
              groupInternal: true,
            });
          },
        });
      }
    }

    items.push({
      icon: <CopyOutlined className={'text-base'} />,
      label: t('Copy {{count}} names', { count: selectedEntries.length }),
      onClick: () => {
        navigator.clipboard.writeText(selectedEntries.map(e => e.name).join('\n'))
          .then(() => {
            Message.success(t('Copied'));
          })
          .catch((e) => {
            Message.error(`${t('Failed to copy')}. ${e}`);
          });
      },
    });

    items.push({
      icon: <CopyOutlined className={'text-base'} />,
      label: t('Copy {{count}} paths', { count: selectedEntries.length }),
      onClick: () => {
        navigator.clipboard.writeText(selectedEntries.map(e => e.path).join('\n'))
          .then(() => {
            Message.success(t('Copied'));
          })
          .catch((e) => {
            Message.error(`${t('Failed to copy')}. ${e}`);
          });
      },
    });
  }


  return (
    <>
      {items.map(i => {
        return (
          <MenuItem onClick={i.onClick}>
            <div className={'flex items-center gap-2'}>
              {i.icon}
              {i.label}
            </div>
          </MenuItem>
        );
      })}
    </>
  );
};
