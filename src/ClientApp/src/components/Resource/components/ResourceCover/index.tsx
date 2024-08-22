import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Checkbox, Dialog, Icon, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import { Img } from 'react-image';
import { LoadingOutlined } from '@ant-design/icons';
import serverConfig from '@/serverConfig';
import { GetResourceCoverURL } from '@/sdk/apis';
import { useTraceUpdate, uuidv4 } from '@/components/utils';
import BApi from '@/sdk/BApi';
import MediaPreviewer from '@/components/MediaPreviewer';
import './index.scss';
import store from '@/store';
import type { CoverSaveLocation } from '@/sdk/constants';
import { ResponseCode } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import { Image, Tooltip } from '@/components/bakaui';

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
  useThumbnail?: boolean;
}

export interface IResourceCoverRef {
  save: (base64Image: string, saveTarget?: CoverSaveLocation) => any;
  load: (refresh?: boolean) => void;
}

const ResourceCover = React.forwardRef((props: Props, ref) => {
  const {
    resourceId,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    disableCache = false,
    disableMediaPreviewer = false,
    biggerCoverPlacement,
    useThumbnail = true
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [url, setUrl] = useState<string>();

  const [previewerVisible, setPreviewerVisible] = useState(false);
  const previewerHoverTimerRef = useRef<any>();

  const disableCacheRef = useRef(disableCache);

  const appContext = store.useModelState('appContext');

  const containerRef = useRef<HTMLDivElement>(null);
  const coverSizeRef = useRef<{w: number; h: number}>({ w: 0, h: 0 });

  useEffect(() => {
    disableCacheRef.current = disableCache;
  }, [disableCache]);

  useEffect(() => {
    loadCover(false);
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
            <div style={{ wordBreak: 'break-word', marginBottom: 10 }}>{rsp.message}</div>
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

  useTraceUpdate(props, '[ResourceCover]');

  const loadCover = useCallback((refresh: boolean) => {
    const serverAddress = appContext.serverAddresses?.[1] ?? serverConfig.apiEndpoint;
    let url = `${serverAddress}/resource/${resourceId}/${useThumbnail ? 'thumbnail' : 'cover'}`;
    if (refresh) {
      url += `?${uuidv4()}`;
    }
    setUrl(url);
  }, []);

  const onClick = useCallback(() => {
    if (propsOnClick) {
      propsOnClick();
    }
  }, [propsOnClick]);


  const renderCover = () => {
    if (url) {
      return (
        <Img
          style={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
          src={url}
          onError={e => {
            console.log(e);
          }}
          onLoad={(e) => {
            setLoaded(true);
            const img = e.target as HTMLImageElement;
            if (img) {
              coverSizeRef.current = { w: img.naturalWidth, h: img.naturalHeight };
            }
            // console.log('loaded', e);
          }}
          loader={(
            <LoadingOutlined className={'text-2xl'} />
          )}
          unloader={(
            <CustomIcon type={'image-slash'} className={'text-2xl'} />
          )}
        />
      );
    }
    return null;
  };

  const renderContainer = () => {
    return (
      <div
        ref={containerRef}
        onClick={onClick}
        className="resource-cover-container"
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

  // console.log(loaded, showBiggerOnHover);

  if (loaded) {
    if (showBiggerOnHover) {
      // ignore small cover
      const containerWidth = containerRef.current?.clientWidth ?? Number.MAX_VALUE;
      const containerHeight = containerRef.current?.clientHeight ?? Number.MAX_VALUE;
      if (coverSizeRef.current.w > containerWidth && coverSizeRef.current.h > containerHeight) {
        return (
          <Tooltip
            placement={biggerCoverPlacement}
            content={(
              <Img
                src={[url!]}
                loader={(
                  <LoadingOutlined className={'text-2xl'} />
                )}
                unloader={(
                  <CustomIcon type={'image-slash'} className={'text-2xl'} />
                )}
                // src={url}
                alt={''}
                style={{
                  maxWidth: window.innerWidth * 0.6,
                  maxHeight: window.innerHeight * 0.6,
                }}
              />
            )}
          >
            {renderContainer()}
          </Tooltip>
        );
      }
    }
  }
  return renderContainer();
});

export default React.memo(ResourceCover);
