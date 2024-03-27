import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Balloon, Checkbox, Dialog, Icon, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import type { BalloonProps } from '@alifd/next/types/balloon';
import { useUpdate } from 'react-use';
import { Img } from 'react-image';
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

interface Props {
  resourceId: number;
  onClick?: () => any;
  showBiggerOnHover?: boolean;
  loadImmediately?: boolean;
  disableCache?: boolean;
  disableMediaPreviewer?: boolean;
}

export interface IResourceCoverRef {
  save: (base64Image: string, saveTarget?: CoverSaveLocation) => any;
  reload: (ct?: AbortSignal) => Promise<any>;
}

const ResourceCover = React.forwardRef((props: Props, ref) => {
  const {
    resourceId,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    loadImmediately = false,
    disableCache = false,
    disableMediaPreviewer = false,
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [loading, setLoading] = useState(true);
  const [cover, setCover] = useState<string | ArrayBuffer | null>(null);
  // No cache will be set after first load
  const loadedOnceRef = useRef(false);
  const biggerCoverAlignRef = useRef<BalloonProps['align']>();

  const [previewerVisible, setPreviewerVisible] = useState(false);
  const previewerHoverTimerRef = useRef<any>();

  const disableCacheRef = useRef(disableCache);

  const appContext = store.useModelState('appContext');

  useEffect(() => {
    disableCacheRef.current = disableCache;
  }, [disableCache]);

  useEffect(() => {
    if (loadImmediately) {
      loadCover(new AbortController().signal).catch(e => {
        Message.error(e.message);
      });
    }
  }, []);

  const saveCoverInternal = useCallback((base64Image: string, overwrite?: boolean, saveLocation?: CoverSaveLocation) => {
    return BApi.resource.saveCover(resourceId, {
      base64Image,
      overwrite,
      saveLocation,
    }, {
      // @ts-ignore
      ignoreError: rsp => rsp.code == ResponseCode.Conflict,
    })
      .then(a => {
        if (!a.code) {
          setCover(base64Image);
          Message.success(t('Cover saved successfully'));
          return a;
        }
        if (a.code == ResponseCode.Conflict) {
          return a;
        }
        throw new Error(a.message!);
      });
  }, []);

  const saveCover = useCallback(async (base64Image: string, saveLocation?: CoverSaveLocation) => {
    const rsp = await saveCoverInternal(base64Image, undefined, saveLocation);
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
          await saveCoverInternal(base64Image, true, saveLocation);
        },
      });
    }
  }, [cover, loading]);

  useImperativeHandle(ref, (): IResourceCoverRef => {
    return {
      save: saveCover,
      reload: loadCover,
    };
  }, [saveCover]);

  useTraceUpdate(props, '[ResourceCover]');

  const loadCover = useCallback((ct?: AbortSignal) => {
    return;
    const serverAddress = appContext.serverAddresses?.[1] ?? serverConfig.apiEndpoint;

    let url = `${serverAddress}${GetResourceCoverURL({ id: resourceId })}`;
    if (disableCacheRef.current) {
      url += `?t=${uuidv4()}`;
    }
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      ct?.addEventListener('abort', () => {
        if (xhr.readyState !== XMLHttpRequest.DONE) {
          xhr.abort();
        }
      });
      xhr.onload = function (e) {
        // console.log('load', e);
      };
      xhr.onerror = (e) => {
        setLoading(false);
        reject(e);
        // console.log('error', e);
      };
      xhr.onabort = (e) => {
        setLoading(false);
        reject(e);
        // console.log('abort', e);
      };
      xhr.onloadend = function (a) {
        // console.log('loadend', xhr);
        loadedOnceRef.current = true;
        if (xhr.response?.size > 0) {
          const reader = new FileReader();
          reader.onloadend = function () {
            setCover(reader.result);
            resolve(reader.result);
          };
          reader.readAsDataURL(xhr.response);
        } else {
          setLoading(false);
          resolve(undefined);
        }
      };
      setLoading(true);
      xhr.open('GET', url);
      if (loadedOnceRef.current) {
        xhr.setRequestHeader('Cache-Control', 'no-cache');
        xhr.setRequestHeader('Pragma', 'no-cache');
      }
      xhr.responseType = 'blob';
      xhr.send();
    });
  }, []);

  const onClick = useCallback(() => {
    if (propsOnClick) {
      propsOnClick();
    }
  }, [propsOnClick]);

  const renderThumbnail = useCallback((data) => {
    return (
      <img
        className={'cover'}
        // @ts-ignore
        src={data}
        alt={''}
        onMouseLeave={() => {
          biggerCoverAlignRef.current = undefined;
        }}
      />
    );
  }, []);

  const renderCover = () => {
    // if (loading) {
    //   return (
    //     <Icon type={'loading'} />
    //   );
    // } else {
      const serverAddress = appContext.serverAddresses?.[1] ?? serverConfig.apiEndpoint;

      let url = `${serverAddress}${GetResourceCoverURL({ id: resourceId })}`;

      return (
        <Img
          src={[url]}
          unloader={(
            <CustomIcon type={'image-slash'} size={'large'} />
          )}
        />
      );
    // }

    // if (cover) {
    //   return renderThumbnail(cover);
    // } else {
    //   if (loading) {
    //     return (
    //       <Icon type={'loading'} />
    //     );
    //   } else {
    //     return (
    //       <CustomIcon type={'image-slash'} size={'large'} />
    //     );
    //   }
    // }
  };

  const renderContainer = () => {
    return (
      <div
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

          const hw = window.innerWidth / 2;
          const hh = window.innerHeight / 2;
          const cx = e.clientX;
          const cy = e.clientY;
          const align = cx > hw ? cy > hh ? 'lt' : 'l' : cy > hh ? 'rt' : 'r';
          if (biggerCoverAlignRef.current != align) {
            biggerCoverAlignRef.current = align;
            forceUpdate();
          }
        }}
        onMouseLeave={() => {
          // console.log('mouse leave');
          clearTimeout(previewerHoverTimerRef.current);
          previewerHoverTimerRef.current = undefined;
          if (previewerVisible) {
            setPreviewerVisible(false);
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

  // console.log('rendering cover, loading: ', loading);

  if (cover) {
    if (showBiggerOnHover) {
      return (
        <Balloon
          trigger={renderContainer()}
          v2
          delay={600}
          closable={false}
          triggerType={'hover'}
          autoFocus={false}
          autoAdjust
          shouldUpdatePosition
          align={biggerCoverAlignRef.current}
        >
          {/* @ts-ignore */}
          <img src={cover} alt={''} style={{ maxWidth: 700, maxHeight: 700 }} />
        </Balloon>
      );
    }
  }
  return renderContainer();
});

export default React.memo(ResourceCover);
