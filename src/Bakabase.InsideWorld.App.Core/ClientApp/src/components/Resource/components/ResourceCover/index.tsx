import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Balloon, Dialog, Icon, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import type { BalloonProps } from '@alifd/next/types/balloon';
import { useUpdate } from 'react-use';
import serverConfig from '@/serverConfig';
import { GetResourceCoverURL } from '@/sdk/apis';
import noCoverImg from '@/assets/no-image-available.svg';
import { useTraceUpdate, uuidv4 } from '@/components/utils';
import BApi from '@/sdk/BApi';
import ResourceDetailDialog from '@/components/Resource/components/DetailDialog';
import MediaPreviewer from '@/components/MediaPreviewer';
import './index.scss';

interface Props {
  resourceId: number;
  onClick?: () => any;
  showBiggerOnHover?: boolean;
  loadImmediately?: boolean;
}

export interface IResourceCoverRef {
  save: (base64Image: string, overwrite: boolean, saveToResourceDirectory: boolean) => any;
  reload: (ct: AbortSignal) => Promise<any>;
}

const Index = React.forwardRef((props: Props, ref) => {
  const {
    resourceId,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    loadImmediately = false,
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

  useEffect(() => {
    if (loadImmediately) {
      loadCover(new AbortController().signal).catch(e => {
        Message.error(e.message);
      });
    }
  }, []);

  const saveCoverInternal = useCallback((base64Image: string, overwrite: boolean, saveToResourceDirectory: boolean) => {
    BApi.resource.saveCover(resourceId, {
      base64Image,
      overwrite,
      saveToResourceDirectory,
    })
      .then(a => {
        if (!a.code) {
          setCover(base64Image);
          return a;
        }
        throw new Error(a.message!);
      });
  }, []);

  const saveCover = useCallback((base64Image: string, overwrite: boolean, saveToResourceDirectory: boolean) => {
    const confirm = (!cover && loading) || !!cover;
    if (overwrite || !confirm) {
      saveCoverInternal(base64Image, overwrite, saveToResourceDirectory);
    } else {
      Dialog.confirm({
        title: t('Sure to set new cover?'),
        content: t('Previous cover file (cover.*) will be overwritten if it exists.'),
        closeMode: ['mask', 'close', 'esc'],
        v2: true,
        onOk: () => saveCoverInternal(base64Image, true, saveToResourceDirectory),
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

  const loadCover = useCallback((ct: AbortSignal) => {
    const url = `${serverConfig.apiEndpoint}${GetResourceCoverURL({ id: resourceId })}?t=`;
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      ct.addEventListener('abort', () => {
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
    if (cover) {
      return renderThumbnail(cover);
    } else {
      if (loading) {
        return (
          <Icon type={'loading'} />
        );
      } else {
        return renderThumbnail(noCoverImg);
      }
    }
  };

  const renderContainer = () => {
    return (
      <div
        onClick={onClick}
        className="resource-cover-container"
        onMouseOver={(e) => {
          console.log('mouse over');
          if (!previewerHoverTimerRef.current) {
            previewerHoverTimerRef.current = setTimeout(() => {
              setPreviewerVisible(true);
            }, 1000);
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
          console.log('mouse leave');
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

  console.log('rendering cover, loading: ', loading);

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

export default React.memo(Index);
