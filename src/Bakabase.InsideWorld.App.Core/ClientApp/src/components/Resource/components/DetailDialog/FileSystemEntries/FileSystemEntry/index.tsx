import { Message } from '@alifd/next';
import i18n from 'i18next';
import React from 'react';
import { IwFsType } from '@/sdk/constants';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import serverConfig from '@/serverConfig';
import CustomIcon from '@/components/CustomIcon';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';

type Props = {
  entry: Entry;
  onEnterDirectory: (path: string) => void;
};

// todo: hardcode url
const buildImageUrl = (path: string) => `${serverConfig.apiEndpoint}/file/play?fullname=${encodeURIComponent(path)}`;

export default ({ entry, onEnterDirectory }: Props) => {
  let comp;
  switch (entry.type) {
    case IwFsType.Directory:
      comp = (
        <svg aria-hidden="true">
          <use xlinkHref="#icon-folder1" />
        </svg>
      );
      break;
    case IwFsType.Image:
      comp = (
        <img src={buildImageUrl(entry.path)} />
      );
      break;
    case IwFsType.Invalid:
      comp = (
        <CustomIcon type={'close-circle'} />
      );
      break;
    case IwFsType.CompressedFileEntry:
    case IwFsType.CompressedFilePart:
    case IwFsType.Symlink:
    case IwFsType.Video:
    case IwFsType.Audio:
    case IwFsType.Unknown:
      comp = (
        <FileSystemEntryIcon path={entry.path} />
      );
      break;
  }

  return (
    <div className={'entry'}>
      <div className="square">
        <div
          className="cover-container"
          onMouseDown={(evt) => {
            evt.preventDefault();
          }}
          onDoubleClick={(evt) => {
            evt.preventDefault();
            evt.stopPropagation();
            if (entry.type == IwFsType.Directory) {
              onEnterDirectory(entry.path);
            } else {
              Message.error(i18n.t('Under development'));
            }
          }}
        >
          {comp}
        </div>
      </div>
      <div className="name">{entry.name}</div>
    </div>
  );
};
