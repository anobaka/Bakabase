import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate, useUpdateEffect } from 'react-use';
import { Img } from 'react-image';
import { LoadingOutlined } from '@ant-design/icons';
import serverConfig from '@/serverConfig';
import { buildLogger, uuidv4 } from '@/components/utils';
import MediaPreviewer from '@/components/MediaPreviewer';
import './index.scss';
import store from '@/store';
import { CoverFit } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import { Carousel, Tooltip } from '@/components/bakaui';
import type { Resource as ResourceModel } from '@/core/models/Resource';

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

type Props = {
  resource: ResourceModel;
  onClick?: () => any;
  showBiggerOnHover?: boolean;
  useCache?: boolean;
  disableMediaPreviewer?: boolean;
  biggerCoverPlacement?: TooltipPlacement;
  coverFit?: CoverFit;
};

export interface IResourceCoverRef {
  load: (disableBrowserCache?: boolean) => void;
}

const log = buildLogger('ResourceCover');

const ResourceCover = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    useCache = false,
    disableMediaPreviewer = false,
    biggerCoverPlacement,
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

  const disableCacheRef = useRef(useCache);

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
    disableCacheRef.current = useCache;
  }, [useCache]);

  useEffect(() => {
    loadCover(false);

    const resizeObserver = new ResizeObserver(() => {
      // Do what you want to do when the size of the element changes
      forceUpdate();
    });
    resizeObserver.observe(containerRef.current!);
    return () => resizeObserver.disconnect(); // clean up
  }, []);

  useImperativeHandle(ref, (): IResourceCoverRef => {
    return {
      load: loadCover,
    };
  }, []);

  // useTraceUpdate(props, '[ResourceCover]');

  const loadCover = useCallback((disableBrowserCache: boolean) => {
    const serverAddresses = appContext.serverAddresses ?? [serverConfig.apiEndpoint];
    const serverAddress = serverAddresses[serverAddresses.length - 1];
    const urls: string[] = [];

    if (useCache) {
      const cps = resource.coverPaths ?? [];
      if (cps.length == 0) {
        cps.push(...(resource.cache?.coverPaths ?? []));
      }
      if (cps.length == 0) {
        cps.push(resource.path);
      }
      urls.push(...cps.map(coverPath => `${serverAddress}/tool/thumbnail?path=${encodeURIComponent(coverPath)}`));
    } else {
      urls.push(`${serverAddress}/resource/${resource.id}/cover`);
    }

    if (disableBrowserCache) {
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
          <MediaPreviewer resourceId={resource.id} />
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
