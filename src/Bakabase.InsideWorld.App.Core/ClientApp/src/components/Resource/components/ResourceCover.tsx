import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Balloon, Dialog, Icon, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import serverConfig from '@/serverConfig';
import { GetResourceCoverURL } from '@/sdk/apis';
import noCoverImg from '@/assets/no-image-available.svg';
import { useTraceUpdate, uuidv4 } from '@/components/utils';
import BApi from '@/sdk/BApi';

interface Props {
  resourceId: number;
  onClick?: () => any;
  showBiggerOnHover?: boolean;
  loadImmediately?: boolean;
}

export interface IResourceCoverRef {
  save: (base64Image: string, overwrite: boolean) => any;
  reload: (ct: AbortSignal) => Promise<any>;
}

const ResourceCover = React.forwardRef((props: Props, ref) => {
  const {
    resourceId,
    onClick: propsOnClick,
    showBiggerOnHover = true,
    loadImmediately = false,
  } = props;
  const { t } = useTranslation();
  const [loading, setLoading] = useState(true);
  const [cover, setCover] = useState<string | ArrayBuffer | null>(null);
  // No cache will be set after first load
  const loadedOnceRef = useRef(false);

  useEffect(() => {
    if (loadImmediately) {
      loadCover(new AbortController().signal).catch(e => {
        Message.error(e.message);
      });
    }
  }, []);

  const saveCoverInternal = useCallback((base64Image: string, overwrite: boolean) => {
    BApi.resource.saveCover(resourceId, {
      base64Image,
      overwrite,
    })
      .then(a => {
        if (!a.code) {
          setCover(base64Image);
          return a;
        }
        throw new Error(a.message!);
      });
  }, []);

  const saveCover = useCallback((base64Image: string, overwrite: boolean) => {
    const confirm = (!cover && loading) || !!cover;
    if (overwrite || !confirm) {
      saveCoverInternal(base64Image, overwrite);
    } else {
      Dialog.confirm({
        title: t('Sure to set new cover?'),
        closeMode: ['mask', 'close', 'esc'],
        v2: true,
        onOk: () => saveCoverInternal(base64Image, true),
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

  const renderThumbnail = useCallback((data, onClick) => {
    return (
      <img
        className={'cover'}
        // @ts-ignore
        src={data}
        alt={''}
        onClick={onClick}
      />
    );
  }, []);

  console.log('rendering cover, loading: ', loading);

  if (cover) {
    if (showBiggerOnHover) {
      return (
        <Balloon
          trigger={renderThumbnail(cover, onClick)}
          v2
          delay={600}
          closable={false}
          triggerType={'hover'}
          autoFocus={false}
          autoAdjust
          shouldUpdatePosition
        >
          {/* @ts-ignore */}
          <img src={cover} alt={''} />
        </Balloon>
      );
    } else {
      return renderThumbnail(cover, onClick);
    }
  } else {
    if (loading) {
      return (
        <Icon type={'loading'} />
      );
    } else {
      return renderThumbnail(noCoverImg, onClick);
    }
  }
});

export default React.memo(ResourceCover);
