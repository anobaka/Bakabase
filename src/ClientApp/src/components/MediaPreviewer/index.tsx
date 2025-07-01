import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from '@alifd/next';
import { useUpdate, useUpdateEffect } from 'react-use';
import { EyeInvisibleOutlined, LoadingOutlined } from '@ant-design/icons';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';
import BApi from '@/sdk/BApi';
import { MediaType } from '@/sdk/constants';
import ReactPlayer from 'react-player';
import serverConfig from '@/serverConfig';
import { PlayFileURL } from '@/sdk/apis';

enum PreviewerStatus {
  Initializing = 0,
  Loading = 1,
  Playing = 2,
  Paused = 3,
  NothingToPreview = 4,
}

interface IItem {
  duration: number;
  filePath: string;
  type: MediaType;
  startFrame: number;
  endFrame: number;
}

interface IProps {
  // resource previewing only for now
  resourceId: number;
}

const MediaPreviewer = (props: IProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { resourceId } = props;

  const [items, setItems] = useState<IItem[]>([]);
  const itemsRef = useRef(items);
  const [status, setStatus] = useState(PreviewerStatus.Initializing);
  const statusRef = useRef<PreviewerStatus>(status);
  const [mouseOffsetX, setMouseOffsetX] = useState<number>();
  const mouseOffsetXRef = useRef(mouseOffsetX);
  const [currentFrame, setCurrentFrame] = useState<number>(-1);
  const currentFrameRef = useRef(currentFrame);
  const [currentItem, setCurrentItem] = useState<IItem>();
  const currentItemRef = useRef(currentItem);

  const autoPlayTimeoutRef = useRef<any>();

  const mediaRef = useRef<HTMLDivElement>(null);
  const reactPlayerRef = useRef<ReactPlayer>(null);
  const videoInitializedRef = useRef(false);

  const isControlled = useCallback(() => mouseOffsetXRef.current != undefined, []);

  useUpdateEffect(() => {
    statusRef.current = status;

    switch (status) {
      case PreviewerStatus.Initializing:
        break;
      case PreviewerStatus.Loading:
        break;
      case PreviewerStatus.Playing:
        resume();
        break;
      case PreviewerStatus.Paused:
        pauseAutomatically();
        break;
    }
  }, [status]);

  useUpdateEffect(() => {
    // console.log('setting mouse offset', mouseOffsetX);
    mouseOffsetXRef.current = mouseOffsetX;
    if (mouseOffsetX != undefined) {
      const percent = progressBarRef.current ? mouseOffsetX * 100 / progressBarRef.current.clientWidth : 0;
      const totalFrameCount = items.reduce((prev, curr) => prev + curr.duration, 0);
      const cf = Math.ceil(totalFrameCount * percent / 100);
      if (cf != currentFrameRef.current) {
        setCurrentFrame(cf);
      }
    }
  }, [mouseOffsetX]);

  useEffect(() => {
    BApi.resource.getResourceDataForPreviewer(resourceId, {
      ignoreError: r => r.code == 404,
    }).then((rsp) => {
      if (rsp.data && rsp.data?.length > 0) {
        const newItems: IItem[] = [];
        let prevFrame = 0;
        for (const d of rsp.data) {
          const item: IItem = {
            duration: d.duration!,
            filePath: d.filePath!,
            // @ts-ignore
            type: d.type!,
            startFrame: prevFrame,
            endFrame: prevFrame + d.duration! - 1,
          };
          newItems.push(item);
          prevFrame = item.endFrame + 1;
        }
        setItems(newItems);
        setStatus(PreviewerStatus.Playing);
        setCurrentFrame(0);
      } else {
        setStatus(PreviewerStatus.NothingToPreview);
      }
    });
  }, []);

  useEffect(() => {
    itemsRef.current = items;
    // console.log('items', items);
  }, [items]);

  useEffect(() => {
    // console.log('Current frame', currentFrame);
    currentFrameRef.current = currentFrame;
    const item = itemsRef.current.find(item => item.startFrame <= currentFrame && item.endFrame >= currentFrame);
    if (item) {
      if (mouseOffsetXRef.current != undefined) {
        if (item == currentItemRef.current) {
          // console.log(reactPlayerRef.current, currentFrame - item.startFrame);
          if (reactPlayerRef.current) {
            // console.log('seeking video');
            reactPlayerRef.current.seekTo((currentFrame - item.startFrame), 'seconds');
          }
        } else {
          setCurrentItem(item);
        }
      } else {
        // auto play
        if (item.startFrame == currentFrame) {
          setStatus(PreviewerStatus.Playing);
          setCurrentItem(item);
        }
      }
    }
  }, [currentFrame]);

  useUpdateEffect(() => {
    // console.log('currentItem', currentItem);
    videoInitializedRef.current = false;
    currentItemRef.current = currentItem;
  }, [currentItem]);

  const progressBarRef = useRef<HTMLDivElement>(null);
  const totalFrameCount = items.reduce((prev, curr) => prev + curr.duration, 0);
  let percentProgress: number = mouseOffsetX != undefined
    ? progressBarRef.current ? mouseOffsetX * 100 / progressBarRef.current.clientWidth : 0
    : totalFrameCount == 0 ? 0 : (currentFrame + 1) * 100 / totalFrameCount;

  const renderMedia = () => {
    if (currentItem) {
      switch (currentItem.type) {
        case MediaType.Image:
          return (
            <img
              src={`${serverConfig.apiEndpoint}${PlayFileURL({
                fullname: currentItem.filePath,
              })}`}
              onLoad={e => {
                clearTimeout(autoPlayTimeoutRef.current);
                if (currentFrame < totalFrameCount && statusRef.current == PreviewerStatus.Playing) {
                  // console.log('Auto play next frame');
                  autoPlayTimeoutRef.current = setTimeout(() => {
                    setCurrentFrame(currentFrame + 1);
                  }, 1000);
                }
              }}
            />
          );
        case MediaType.Video: {
          const playing = status == PreviewerStatus.Playing && !isControlled();
          return (
            <ReactPlayer
              width={mediaRef.current?.clientWidth}
              height={mediaRef.current?.clientHeight}
              ref={reactPlayerRef}
              url={`${serverConfig.apiEndpoint}${PlayFileURL({
                fullname: currentItem.filePath,
              })}`}
              onDuration={e => {
                // console.log('duration', e);
              }}
              onProgress={e => {
                // console.log('On progress', e);
                if (!isControlled()) {
                  const frame = Math.ceil(e.playedSeconds);
                  const nextFrame = frame + currentItem.startFrame - 1;
                  if (nextFrame != currentFrame) {
                    setCurrentFrame(nextFrame);
                  }
                }
              }}
              onEnded={() => {
                // console.log('Video ended');
                if (!isControlled()) {
                  const nextItem = items.find(item => item.startFrame > currentItem.startFrame);
                  if (nextItem) {
                    setCurrentFrame(nextItem.startFrame);
                  } else {
                    setCurrentFrame(currentItem!.endFrame);
                  }
                }
              }}
              onReady={e => {
                if (!videoInitializedRef.current) {
                  if (isControlled()) {
                    const seekTo = currentFrame - currentItem.startFrame;
                    if (seekTo > 0) {
                      // console.log(`Auto seek video to ${currentFrame - currentItem.startFrame}s`);
                      e.seekTo(seekTo, 'seconds');
                    }
                  }
                  videoInitializedRef.current = true;
                }
              }}
              muted
              playing={playing}
              controls={false}
              config={{
                file: {
                  attributes: {
                    crossOrigin: 'anonymous',
                  },
                },
              }}
            />
          );
        }
        case MediaType.Audio:
        case MediaType.Text:
        case MediaType.Unknown:
        default:
          return (
            <div>{t('Unsupported')}</div>
          );
      }
    }
    return;
  };

  const pauseAutomatically = useCallback(() => {
    clearTimeout(autoPlayTimeoutRef.current);
    autoPlayTimeoutRef.current = undefined;
  }, []);

  const resume = useCallback(() => {
    if (!autoPlayTimeoutRef.current) {
      if (currentItemRef.current?.type == MediaType.Image) {
        autoPlayTimeoutRef.current = setTimeout(() => {
          const totalFrameCount = itemsRef.current.reduce((prev, curr) => prev + curr.duration, 0);
          if (currentFrameRef.current < totalFrameCount) {
            setCurrentFrame(currentFrameRef.current + 1);
          }
        }, 1000);
      }
    }
  }, []);

  const renderCore = () => {
    // console.log('renderCore', status, currentItem, currentFrame);
    switch (status) {
      case PreviewerStatus.NothingToPreview:
        return (
          <div className={'nothing-to-preview'}>
            <div className="mask" />
            <div className="label">
              <EyeInvisibleOutlined className={'text-2xl'} />
            </div>
          </div>
        );
      case PreviewerStatus.Initializing:
      case PreviewerStatus.Loading:
        return (
          <div className={'loading'}>
            <div className="mask" />
            <div className="label">
              <LoadingOutlined className={'text-2xl'} />
              {/* <Icon type={'loading'} size={'xl'} /> */}
              {/* {t(PreviewerStatus[status])} */}
            </div>
          </div>
        );
      case PreviewerStatus.Playing:
      case PreviewerStatus.Paused:
        return (
          <div className={'playing'}>
            {status == PreviewerStatus.Paused && (
              <div
                className={'paused-cover'}
                onClick={() => {
                  setStatus(PreviewerStatus.Playing);
                }}
              >
                <CustomIcon type={'timeout'} className={'text-3xl'} />
              </div>
            )}
            <div
              className="media"
              ref={mediaRef}
              onClick={() => {
                setStatus(PreviewerStatus.Paused);
              }}
            >{renderMedia()}</div>
            <div
              className="progress-bar"
              ref={progressBarRef}
              onMouseMove={e => {
                const { left, width } = progressBarRef.current!.getBoundingClientRect()!;
                const offsetX = Math.ceil(e.clientX - left);
                if (offsetX != mouseOffsetX) {
                  setMouseOffsetX(offsetX);
                }
                pauseAutomatically();
              }}
              onMouseLeave={e => {
                if (mouseOffsetXRef.current != undefined) {
                  // set by effect may cause mismatched status
                  mouseOffsetXRef.current = undefined;
                  setMouseOffsetX(undefined);
                }
                resume();
              }}
            >
              <div className="bar">
                <div className="progress" style={{ width: `${percentProgress}%` }} />
              </div>
            </div>
          </div>
        );
    }
  };

  return (
    <div className={'media-previewer'}>
      {renderCore()}
    </div>
  );
};

export default MediaPreviewer;
