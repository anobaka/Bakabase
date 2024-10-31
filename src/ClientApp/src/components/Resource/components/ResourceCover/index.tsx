import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Checkbox, Dialog, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useUpdate, useUpdateEffect } from 'react-use';
import { Img } from 'react-image';
import { LoadingOutlined } from '@ant-design/icons';
import serverConfig from '@/serverConfig';
import { buildLogger, uuidv4 } from '@/components/utils';
import BApi from '@/sdk/BApi';
import MediaPreviewer from '@/components/MediaPreviewer';
import './index.scss';
import store from '@/store';
import type { CoverSaveLocation } from '@/sdk/constants';
import { CoverFit, ResponseCode } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import { Carousel, Tooltip } from '@/components/bakaui';

type TooltipPlacement =
  | 'top'
  | 'bottom'
  | 'right'
  | 'left'
  | 'top-start'
  | 'top-end'
  | 'bottom-start'
  | 'bottom-end'
  | 'left-start'
  | 'left-end'
  | 'right-start'
  | 'right-end';

interface Props {
  resourceId: number;
  onClick?: () => any;
  showBiggerOnHover?: boolean;
  disableCache?: boolean;
  disableMediaPreviewer?: boolean;
  biggerCoverPlacement?: TooltipPlacement;
  coverPaths?: string[];
  coverFit?: CoverFit;
}

export interface IResourceCoverRef {
  save: (base64Image: string, saveTarget?: CoverSaveLocation) => any;
  load: (refresh?: boolean) => void;
}

const log = buildLogger('ResourceCover');

const ResourceCover = React.forwardRef((props: Props, ref) => {
  const {
    resourceId,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    disableCache = false,
    disableMediaPreviewer = false,
    biggerCoverPlacement,
    coverPaths,
    coverFit = CoverFit.Contain,
  } = props;
  // log('rendering', props);
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [urls, setUrls] = useState<string[]>();

  const [previewerVisible, setPreviewerVisible] = useState(false);
  const previewerHoverTimerRef = useRef<any>();

  const disableCacheRef = useRef(disableCache);

  const appContext = store.useModelState('appContext');

  const containerRef = useRef<HTMLDivElement>(null);
  const maxCoverRawSizeRef = useRef<{ w: number; h: number }>({
    w: 0,
    h: 0,
  });

  // log(coverFit);

  useUpdateEffect(() => {
    forceUpdate();
  }, [coverFit]);

  useEffect(() => {
    // log('urls changed', urls);
  }, [urls]);

  useEffect(() => {
    disableCacheRef.current = disableCache;
  }, [disableCache]);

  useEffect(() => {
    loadCover(false);

    const resizeObserver = new ResizeObserver(() => {
      // Do what you want to do when the size of the element changes
      forceUpdate();
    });
    resizeObserver.observe(containerRef.current!);
    return () => resizeObserver.disconnect(); // clean up
  }, []);

  const saveThumbnailInternal = useCallback((base64Image: string, overwrite?: boolean, saveLocation?: CoverSaveLocation) => {
    return BApi.resource.saveThumbnail(resourceId, {
      base64Image,
      overwrite,
      saveLocation,
    }, {
      // @ts-ignore
      ignoreError: rsp => rsp.code == ResponseCode.Conflict,
    })
      .then(a => {
        if (!a.code) {
          loadCover(true);
          Message.success(t('Cover saved successfully'));
          return a;
        }
        if (a.code == ResponseCode.Conflict) {
          return a;
        }
        throw new Error(a.message!);
      });
  }, []);

  const saveThumbnail = useCallback(async (base64Image: string, saveLocation?: CoverSaveLocation) => {
    const rsp = await saveThumbnailInternal(base64Image, undefined, saveLocation);
    if (rsp.code == ResponseCode.Conflict) {
      let remember = false;
      Dialog.confirm({
        title: t('Sure to set new cover?'),
        content: (
          <div>
            <div>{t('Current cover file will be overwritten.')}</div>
            <div style={{
              wordBreak: 'break-word',
              marginBottom: 10,
            }}
            >{rsp.message}</div>
            <Checkbox label={t('Remember my choice')} onChange={c => remember = c} />
          </div>
        ),
        closeMode: ['mask', 'close', 'esc'],
        v2: true,
        onOk: async () => {
          if (remember) {
            const options = store.getModelState('resourceOptions').coverOptions || {};
            await BApi.options.patchResourceOptions({
              coverOptions: {
                ...options,
                overwrite: true,
              },
            });
          }
          await saveThumbnailInternal(base64Image, true, saveLocation);
        },
      });
    }
  }, [loaded, loading]);

  useImperativeHandle(ref, (): IResourceCoverRef => {
    return {
      save: saveThumbnail,
      load: loadCover,
    };
  }, [saveThumbnail]);

  // useTraceUpdate(props, '[ResourceCover]');

  const loadCover = useCallback((refresh: boolean) => {
    const serverAddresses = appContext.serverAddresses ?? [serverConfig.apiEndpoint];
    const serverAddress = serverAddresses[serverAddresses.length - 1];
    const urls: string[] = [];
    if (coverPaths && coverPaths.length > 0) {
      urls.push(...coverPaths.map(coverPath => `${serverAddress}/tool/thumbnail?path=${encodeURIComponent(coverPath)}`));
    } else {
      urls.push(`${serverAddress}/resource/${resourceId}/cover`);
    }
    if (refresh || disableCache) {
      for (let i = 0; i < urls.length; i++) {
        urls[i] += urls[i].includes('?') ? `&v=${uuidv4()}` : `?v=${uuidv4()}`;
      }
    }
    setUrls(urls);
  }, []);

  const onClick = useCallback(() => {
    if (propsOnClick) {
      propsOnClick();
    }
  }, [propsOnClick]);


  const renderCover = useCallback(() => {
    if (urls) {
      return (
        <Carousel
          key={urls.join(',')}
          autoplay={urls && urls.length > 1}
          // autoplay={false}
          dots
        >
          {urls?.map(url => {
            let dynamicClassNames: string[] = [];
            if (containerRef.current && maxCoverRawSizeRef.current) {
              if (maxCoverRawSizeRef.current.w > containerRef.current.clientWidth) {
                dynamicClassNames.push('w-full');
              }
              if (maxCoverRawSizeRef.current.h > containerRef.current.clientHeight) {
                dynamicClassNames.push('h-full');
              }
              dynamicClassNames.push(coverFit == CoverFit.Cover ? 'object-cover' : 'object-contain');
            }

            const dynamicClassName = dynamicClassNames.join(' ');
            return (
              <div key={url}>
                <div
                  style={{
                    width: containerRef.current?.clientWidth,
                    height: containerRef.current?.clientHeight,
                  }}
                  className={'flex items-center justify-center'}
                >
                  <Img
                    className={`${dynamicClassName} max-w-full max-h-full`}
                    key={url}
                    src={url}
                    onError={e => {
                      log(e);
                    }}
                    onLoad={(e) => {
                      setLoaded(true);
                      // forceUpdate();
                      const img = e.target as HTMLImageElement;
                      log('loaded', e, img);
                      if (img) {
                        if (!maxCoverRawSizeRef.current) {
                          maxCoverRawSizeRef.current = {
                            w: img.naturalWidth,
                            h: img.naturalHeight,
                          };
                        } else {
                          maxCoverRawSizeRef.current.w = Math.max(maxCoverRawSizeRef.current.w, img.naturalWidth);
                          maxCoverRawSizeRef.current.h = Math.max(maxCoverRawSizeRef.current.h, img.naturalHeight);
                        }
                      }
                    }}
                    loader={(
                      <LoadingOutlined className={'text-2xl'} />
                    )}
                    unloader={(
                      <CustomIcon type={'image-slash'} className={'text-2xl'} />
                    )}
                  />
                </div>
              </div>
            );
          })}
        </Carousel>
      );
    }
    return null;
  }, [urls, coverFit]);

  const renderContainer = () => {
    return (
      <div
        ref={containerRef}
        onClick={onClick}
        className="resource-cover-container overflow-hidden"
        onMouseOver={(e) => {
          // console.log('mouse over');
          if (!disableMediaPreviewer) {
            if (!previewerHoverTimerRef.current) {
              previewerHoverTimerRef.current = setTimeout(() => {
                setPreviewerVisible(true);
              }, 1000);
            }
          }
        }}
        onMouseLeave={() => {
          // console.log('mouse leave');
          if (!disableMediaPreviewer) {
            clearTimeout(previewerHoverTimerRef.current);
            previewerHoverTimerRef.current = undefined;
            if (previewerVisible) {
              setPreviewerVisible(false);
            }
          }
        }}
      >
        {previewerVisible && (
          <MediaPreviewer resourceId={resourceId} />
        )}
        {renderCover()}
      </div>
    );
  };

  let tooltipWidth: number | undefined;
  let tooltipHeight: number | undefined;
  if (showBiggerOnHover) {
    // ignore small cover
    const containerWidth = containerRef.current?.clientWidth ?? 100;
    const containerHeight = containerRef.current?.clientHeight ?? 100;
    if (maxCoverRawSizeRef.current.w > containerWidth && maxCoverRawSizeRef.current.h > containerHeight) {
      const tooltipScale = Math.min(window.innerWidth * 0.6 / maxCoverRawSizeRef.current.w, window.innerHeight * 0.6 / maxCoverRawSizeRef.current.h);
      tooltipWidth = maxCoverRawSizeRef.current.w * tooltipScale;
      tooltipHeight = maxCoverRawSizeRef.current.h * tooltipScale;
    }
  }

  return (
    <Tooltip
      // key={urls?.join(',')}
      // isOpen
      placement={biggerCoverPlacement}
      isDisabled={tooltipWidth == undefined}
      content={(
        <div style={{
          width: tooltipWidth,
          height: tooltipHeight,
        }}
        >
          <Carousel
            autoplay={urls && urls.length > 1}
            adaptiveHeight
            dots
          >
            {urls?.map(url => (
              <div key={url}>
                <div
                  style={{
                    maxWidth: tooltipWidth,
                    maxHeight: tooltipHeight,
                  }}
                  className={'flex items-center justify-center'}
                >
                  <Img
                    // key={url}
                    src={url}
                    loader={(
                      <LoadingOutlined className={'text-2xl'} />
                    )}
                    unloader={(
                      <CustomIcon type={'image-slash'} className={'text-2xl'} />
                    )}
                    onLoad={e => {
                      log('loaded bigger', e);
                    }}
                    // src={url}
                    alt={''}
                    style={{
                      maxWidth: tooltipWidth,
                      maxHeight: tooltipHeight,
                    }}
                  />
                </div>
              </div>
            ))}
          </Carousel>
        </div>
      )}
    >
      {renderContainer()}
    </Tooltip>
  );
});

export default React.memo(ResourceCover);
