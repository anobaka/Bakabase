import { Button, Message, Overlay } from '@alifd/next';
import React from 'react';
import * as dayjs from 'dayjs';
import * as duration from 'dayjs/plugin/duration';
import type ReactPlayer from 'react-player/lazy';
import MediaPlayer from '@/components/MediaPlayer';
import type { CoverSaveTarget } from '@/sdk/constants';
import { MediaType, PlaylistItemType } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import { captureVideoFrame } from '@/components/utils';
import { PlaylistCollection } from '@/components/Playlist';
import BApi from '@/sdk/BApi';
import FavoritesSelector from '@/pages/Resource/components/FavoritesSelector';
import CoverSaveButton from '@/components/Resource/components/CoverSaveButton';

dayjs.extend(duration);

const { Popup } = Overlay;

export default (resourceId: number, resourcePath: string, onSaveAsNewCover: (base64String: string, saveTarget?: CoverSaveTarget) => any, t: any, isSingleFileResource: boolean) => {
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
                components.push(
                  <CoverSaveButton
                    getDataURL={() => {
                      // Call captureVideoFrame() when you want to record a screenshot
                      const frame = captureVideoFrame(reactPlayer!.getInternalPlayer(), 'png', 1);
                      if (frame) {
                        return frame.dataUri;
                      } else {
                        const msg = t('Failed to capture video frame');
                        Message.error(msg);
                        throw new Error(msg);
                      }
                    }}
                    disabledReason={playing ? t('Available when video is paused') : undefined}
                    isSingleFileResource={isSingleFileResource}
                    onSaveAsNewCover={onSaveAsNewCover}
                  />,
                );
              }
              case MediaType.Image: {
                if (image) {
                  components.push(
                    <CoverSaveButton
                      getDataURL={() => {
                        let canvas = document.createElement('canvas');
                        canvas.width = image.width;
                        canvas.height = image.height;
                        let ctx = canvas.getContext('2d')!;
                        ctx.drawImage(image, 0, 0);
                        return canvas.toDataURL();
                      }}
                      isSingleFileResource={isSingleFileResource}
                      onSaveAsNewCover={onSaveAsNewCover}
                    />,
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
