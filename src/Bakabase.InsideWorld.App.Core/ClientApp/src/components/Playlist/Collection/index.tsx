import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Balloon, Button, Dialog, Input, List, Message } from '@alifd/next';
import * as dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import './index.scss';

import { useUpdate } from 'react-use';
import PlaylistDetail from '@/components/Playlist/Detail/index';
import CustomIcon from '@/components/CustomIcon';
import { PlaylistItemType } from '@/sdk/constants';
import TimeRanger from '@/components/TimeRanger';
import BApi from '@/sdk/BApi';
import MediaPlayer from '@/components/MediaPlayer';

interface IPlaylistItem {
  type: PlaylistItemType;
  resourceId?: number;
  file?: string;
  startTime?: string;
  endTime?: string;
}

class PlaylistItem implements IPlaylistItem {
  type: PlaylistItemType;
  endTime?: string;
  file?: string;
  resourceId?: number;
  startTime?: string;

  constructor(init?: Partial<IPlaylistItem>) {
    Object.assign(this, init);
  }

  static equals(a: IPlaylistItem, b: IPlaylistItem) {
    if (a.file && b.file && a.file == b.file) {
      return true;
    }
    return !!(!a.file && !b.file && a.resourceId && b.resourceId && a.resourceId == b.resourceId);
  }
}

interface IPropsItem {
  type: PlaylistItemType;
  resourceId?: number;
  file?: string;
  totalSeconds?: number;
  onTimeSelected?: (second: number) => void;
}

interface IPlaylist {
  id?: number;
  name?: string;
  items?: IPlaylistItem[];
  interval?: number;
  order?: number;
}

interface IProps {
  defaultNewItem?: IPropsItem;
  className?: string;
}

export default ({
                  defaultNewItem,
                  className,
                }: IProps) => {
  const [playlists, setPlaylists] = useState<IPlaylist[]>([]);
  const newItemRef = useRef<IPlaylistItem | undefined>(defaultNewItem ? {
    ...defaultNewItem,
  } : undefined);

  const forceUpdate = useUpdate();

  const { t } = useTranslation();

  useEffect(() => {
    loadPlaylists();
  }, []);

  const loadPlaylists = useCallback(() => {
    BApi.playlist.getAllPlaylists()
      .then(a => {
        // @ts-ignore
        setPlaylists(a.data || []);
      });
  }, []);

  const saveItemToPlaylist = useCallback((item: IPlaylistItem, playlist: IPlaylist) => {
    const items = playlist.items?.slice() || [];
    items.push(item);
    return BApi.playlist.putPlaylist({
      ...playlist,
      // @ts-ignore
      items,
    })
      .then((a) => {
        if (!a.code) {
          playlist.items = items;
          forceUpdate();
          Message.success(t('Add successfully'));
          return a;
        }
        throw new Error(a.message!);
      });
  }, []);

  const setDurationTime = useCallback((currentValue: number[], newValue: number[] | undefined, key: 'startTime' | 'endTime') => {
    if (newValue) {
      const idx = key == 'startTime' ? 0 : 1;
      const value = newValue[idx];
      if (value != undefined && currentValue[idx] != value) {
        currentValue[idx] = value;
        newItemRef.current![key] = dayjs.duration(value * 1000)
          .format('HH:mm:ss');
        if (defaultNewItem?.onTimeSelected) {
          defaultNewItem.onTimeSelected(value);
        }
      }
    }
  }, []);

  const showDurationSelector = useCallback((item: IPlaylistItem, playlist: IPlaylist) => {
    // console.log(playerElement, playerElement?.currentTime);
    const totalSeconds = Math.floor(defaultNewItem!.totalSeconds || 0);
    if (totalSeconds == 0) {
      return Message.error(t('Can not get duration of a media file'));
    }
    const duration = [0, totalSeconds];
    Dialog.show({
      title: t('Select duration'),
      id: 'playlist-item-duration-selector',
      content: (
        <div className={'playlist-item-duration-selector'}>
          <TimeRanger
            duration={dayjs.duration(totalSeconds * 1000)}
            onProcess={(seconds) => {
              setDurationTime(duration, seconds, 'startTime');
              setDurationTime(duration, seconds, 'endTime');
            }}
          />
        </div>
      ),
      closeMode: ['close', 'esc', 'mask'],
      onOk: () => saveItemToPlaylist(item, playlist),
    });
  }, []);

  const addNewItem = useCallback((newItem: IPlaylistItem, playlist: any) => {
    if (newItem) {
      switch (newItem.type) {
        case PlaylistItemType.Resource:
        case PlaylistItemType.Image:
          saveItemToPlaylist(newItem, playlist);
          break;
        case PlaylistItemType.Video:
        case PlaylistItemType.Audio:
          showDurationSelector(newItem, playlist);
          break;
      }
    }
  }, []);

  const renderAddButton = useCallback((newItem: IPlaylistItem | undefined, playlist: IPlaylist) => {
    if (newItem) {
      const added = playlist.items?.find(a => PlaylistItem.equals(a, newItem));
      const btn = (
        <Button
          size={'small'}
          type={'normal'}
          onClick={() => {
            addNewItem(newItem, playlist);
          }}
        >
          <CustomIcon type={'plus-circle'} size={'small'} />
          {t(added ? 'Added' : 'Add it here')}
        </Button>
      );
      if (added) {
        return (
          <Balloon.Tooltip
            trigger={btn}
            triggerType={'hover'}
            v2
          >
            {t('You can add it more than once, and if you want to remove it from playlist, you should click the corresponding playlist first.')}
          </Balloon.Tooltip>
        );
      }
      return btn;
    }
    return;
  }, []);

  return (
    <div className={`playlist-collection ${className}`}>
      <List
        size="small"
        // header={<div>Notifications</div>}
        dataSource={playlists}
        renderItem={(pl, i) => (
          <List.Item
            key={i}
            extra={(
              <>
                {renderAddButton(newItemRef.current, pl)}
                {pl.items?.length > 0 && (
                  <Button
                    size={'small'}
                    onClick={() => {
                      BApi.playlist.getPlaylistFiles(pl.id)
                        .then((a) => {
                          if (a.data) {
                            if (a.data.length > 0) {
                              const files: {path: string; startTime?: string; endTime?: string}[] = [];
                              // console.log(a.data, pl, pl.items, pl.items.length);

                              for (let x = 0; x < pl.items.length; x++) {
                                const item = pl.items[x];
                                const currentItemFiles = a.data[x];
                                if (currentItemFiles) {
                                  for (const file of a.data[x]) {
                                    files.push({
                                      path: file,
                                      startTime: item.startTime,
                                      endTime: item.endTime,
                                    });
                                  }
                                }
                              }
                              const mpProps = {
                                files,
                                interval: pl.interval,
                                autoPlay: true,
                                style: {
                                  zIndex: 1001,
                                },
                              };
                              MediaPlayer.show(mpProps);
                            } else {
                              Message.error(t('No playable contents'));
                            }
                          }
                        });
                    }}
                  >
                    <CustomIcon type={'play-circle'} size={'small'} />
                    {t('Play')}
                  </Button>
                )}
                <Button
                  warning
                  size={'small'}
                  onClick={() => {
                    Dialog.confirm({
                      title: t('Sure to delete?'),
                      onOk: () => BApi.playlist.deletePlaylist(pl.id)
                        .then(a => {
                          if (!a.code) {
                            loadPlaylists();
                            return a;
                          }
                          throw new Error(a.message!);
                        }),
                    });
                  }}
                >
                  <CustomIcon type={'delete'} size={'small'} />
                  {t('Delete')}
                </Button>
              </>
            )}
            title={
              <Button
                onClick={() => {
                  const dialog = Dialog.show({
                    title: t('Details of {{name}}', { name: pl.name }),
                    closeMode: ['esc', 'mask', 'close'],
                    v2: true,
                    width: 800,
                    content: (
                      <PlaylistDetail
                        id={pl.id}
                        onChange={(p) => {
                          Object.assign(pl, p);
                          console.log(playlists, pl, p);
                          setPlaylists([...playlists]);
                        }}
                      />
                    ),
                    onOk: () => BApi.playlist.putPlaylist(pl)
                      .then(a => {
                        if (!a.code) {
                          dialog.hide();
                          return a;
                        }
                        throw new Error(a.message!);
                      }),
                  });
                }}
                type={'primary'}
                text
              >{pl.name}({(pl.items || []).length})
              </Button>
            }
          />
        )}
      />
      <div className="opt">
        <Button
          type={'normal'}
          size={'small'}
          onClick={() => {
            let name;
            Dialog.show({
              title: t('Creating playlist'),
              closeable: true,
              content: (
                <Input onChange={(v) => {
                  name = v;
                }}
                />
              ),
              onOk: () => new Promise((resolve, reject) => {
                if (!name) {
                  Message.error(t('Name can not be empty'));
                  reject();
                  return;
                }
                BApi.playlist.addPlaylist({
                  name,
                })
                  .then(a => {
                    if (!a.code) {
                      loadPlaylists();
                      resolve(a);
                    } else {
                      reject();
                    }
                  });
              }),
            });
          }}
        >
          {t('Create')}
        </Button>
      </div>
    </div>
  );
};
