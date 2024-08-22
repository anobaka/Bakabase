import { SortableElement } from 'react-sortable-hoc';
import { Button } from '@alifd/next';
import React from 'react';
import CustomIcon from '@/components/CustomIcon';
import { OpenFileOrDirectory } from '@/sdk/apis';
import { PlaylistItemType } from '@/sdk/constants';
import DragHandle from '@/components/DragHandle';
import ClickableIcon from '@/components/ClickableIcon';
import BApi from '@/sdk/BApi';

const ItemTypeIcon = {
  [PlaylistItemType.Resource]: 'detail',
  [PlaylistItemType.Video]: 'video',
  [PlaylistItemType.Image]: 'image',
  [PlaylistItemType.Audio]: 'audio',
};


const renderDuration = (item) => {
  if ((item.type == PlaylistItemType.Audio || item.type == PlaylistItemType.Video)) {
    let sb = item.startTime ?? '';
    if (item.endTime) {
      sb += ` ~ ${item.endTime}`;
    }
    return sb;
  }
};

export default SortableElement(({
                                  item,
                                  resource,
                                  onRemove,
                                }) => {
  let displayFilename = item.file;
  if (resource && displayFilename) {
    if (resource.isSingleFile) {
      displayFilename = item.file?.replace(resource?.path, '')
        .trim('/')
        .trim('\\');
    } else {
      const segments = item.file?.replaceAll('\\', '/')
        .split('/');
      console.log(segments);
      displayFilename = segments[segments.length - 1];
    }
  }
  // console.log(resource, displayFilename);

  return (
    <div className={'sortable-playlist-item'}>
      <div className="resource-name">
        <DragHandle />
        <div className="type">
          <CustomIcon type={ItemTypeIcon[item.type]} size={'small'} />
        </div>
        <div className="name">
          <Button
            type={'primary'}
            text
            onClick={() => {
              if (resource) {
                BApi.tool.openFileOrDirectory({
                  path: item.file ?? resource.rawFullname,
                  openInDirectory: item.file ? true : resource.isSingleFile,
                });
              }
            }}
          >
            {resource?.displayName}
          </Button>
        </div>
      </div>
      <div className="file">{displayFilename}</div>

      <div className="duration">
        {renderDuration(item)}
      </div>
      <div className="opt">
        <ClickableIcon
          colorType={'danger'}
          type={'delete'}
          onClick={() => {
            onRemove(item);
          }}
        />
      </div>
    </div>
  );
});
