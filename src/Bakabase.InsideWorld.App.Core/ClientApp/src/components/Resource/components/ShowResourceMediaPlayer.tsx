import { Balloon, Button, Message, Overlay } from '@alifd/next';
import React from 'react';
import * as dayjs from 'dayjs';
import * as duration from 'dayjs/plugin/duration';
import type ReactPlayer from 'react-player/lazy';
import MediaPlayer from '@/components/MediaPlayer';
import { MediaType, PlaylistItemType } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import { captureVideoFrame } from '@/components/utils';
import { PlaylistCollection } from '@/components/Playlist';
import BApi from '@/sdk/BApi';
import FavoritesSelector from '@/pages/Resource/components/FavoritesSelector';

dayjs.extend(duration);

const { Popup } = Overlay;

export default (resourceId: number, resourcePath: string, onSaveAsNewCover: (base64String: string) => any, t: any) => {
  // const { t } = useTranslation();

  console.log('Showing resource media player');

  BApi.file.getAllFiles({
    path: resourcePath,
  })
    .then(a => {
      if (!a.code && a.data) {
        if (a.data.length == 0) {
          return Message.notice(t('No files to preview'));
        }
        const files = a.data;
        MediaPlayer.show({
          id: 'media-player',
          files: files.map(f => ({
            path: f,
          })),
          defaultActiveIndex: 0,
          renderOperations: (filePath: string, mediaType: MediaType, playing: boolean, reactPlayer: ReactPlayer | null, image: HTMLImageElement | null): any => {
            console.log(filePath, mediaType, playing, reactPlayer);
            console.log(reactPlayer, reactPlayer?.getDuration());
            const components = [
              (
                <Button
                  type={'normal'}
                  onClick={() => {
                    FavoritesSelector.show({
                      resourceIds: [resourceId],
                    });
                  }}
                >
                  <CustomIcon type={'star'} />
                  {t('Add resource to favorites')}
                </Button>
              )];
            if (mediaType == MediaType.Image || mediaType == MediaType.Audio || mediaType == MediaType.Video) {
              let playlistItemType: PlaylistItemType = PlaylistItemType.Resource;
              switch (mediaType) {
                case MediaType.Video: {
                  playlistItemType = PlaylistItemType.Video;
                  break;
                }
                case MediaType.Audio: {
                  playlistItemType = PlaylistItemType.Audio;
                  break;
                }
                case MediaType.Image: {
                  playlistItemType = PlaylistItemType.Image;
                  break;
                }
              }

              components.push(
                <Popup
                  v2
                  triggerType={'click'}
                  container={'media-player'}
                  trigger={(
                    <Button type={'normal'}>
                      <CustomIcon type={'playlistadd'} />
                      {t('Add file to playlists')}
                    </Button>
                  )}
                  needAdjust
                  shouldUpdatePosition
                >
                  <PlaylistCollection
                    defaultNewItem={{
                      type: playlistItemType!,
                      file: filePath,
                      resourceId,
                      onTimeSelected: reactPlayer ? s => reactPlayer.seekTo(s) : undefined,
                      totalSeconds: reactPlayer?.getDuration(),
                    }}
                  />
                </Popup>,
              );
            }
            switch (mediaType) {
              case MediaType.Video: {
                const btn = (
                  <Button
                    type={'normal'}
                    disabled={playing}
                    onClick={() => {
                      // Call captureVideoFrame() when you want to record a screenshot
                      const frame = captureVideoFrame(reactPlayer!.getInternalPlayer(), 'png', 1);
                      if (frame) {
                        onSaveAsNewCover(frame.dataUri);
                      } else {
                        Message.error(t('Failed to capture video frame'));
                      }
                    }}
                  >
                    <CustomIcon type={'image-redo'} />
                    {t('Capture as a new cover')}
                  </Button>
                );
                if (playing) {
                  components.push(
                    <Balloon.Tooltip
                      align={'t'}
                      trigger={btn}
                    >
                      {t('Available when video is paused')}
                    </Balloon.Tooltip>,
                  );
                } else {
                  components.push(btn);
                }
              }
              case MediaType.Image: {
                if (image) {
                  components.push(
                    <Button
                      type={'normal'}
                      disabled={playing}
                      onClick={() => {
                        let canvas = document.createElement('canvas');
                        canvas.width = image.width;
                        canvas.height = image.height;
                        let ctx = canvas.getContext('2d')!;
                        ctx.drawImage(image, 0, 0);
                        onSaveAsNewCover(canvas.toDataURL());
                      }}
                    >
                      <CustomIcon type={'image-redo'} />
                      {t('Save as a new cover')}
                    </Button>,
                  );
                }
                break;
              }
            }
            return (
              <div className={'operations'}>
                {components}
              </div>
            );
          },
        });
      }
    });
};
