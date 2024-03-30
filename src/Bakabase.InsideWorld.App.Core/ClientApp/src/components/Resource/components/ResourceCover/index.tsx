import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Balloon, Checkbox, Dialog, Icon, Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import type { BalloonProps } from '@alifd/next/types/balloon';
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

interface Props {
  resourceId: number;
  onClick?: () => any;
  showBiggerOnHover?: boolean;
  disableCache?: boolean;
  disableMediaPreviewer?: boolean;
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
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [url, setUrl] = useState<string>();

  const biggerCoverAlignRef = useRef<BalloonProps['align']>();

  const [previewerVisible, setPreviewerVisible] = useState(false);
  const previewerHoverTimerRef = useRef<any>();

  const disableCacheRef = useRef(disableCache);

  const appContext = store.useModelState('appContext');

  useEffect(() => {
    disableCacheRef.current = disableCache;
  }, [disableCache]);

  useEffect(() => {
    loadCover(false);
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
  }, [loaded, loading]);

  useImperativeHandle(ref, (): IResourceCoverRef => {
    return {
      save: saveCover,
      load: loadCover,
    };
  }, [saveCover]);

  useTraceUpdate(props, '[ResourceCover]');

  const loadCover = useCallback((refresh: boolean) => {
    const serverAddress = appContext.serverAddresses?.[1] ?? serverConfig.apiEndpoint;
    let url = `${serverAddress}${GetResourceCoverURL({ id: resourceId })}`;
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
          src={[url]}
          onLoad={() => {
            setLoaded(true);
            // console.log('loaded');
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

  // console.log(loaded, showBiggerOnHover);

  if (loaded) {
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
          <img src={url} alt={''} style={{ maxWidth: 700, maxHeight: 700 }} />
        </Balloon>
      );
    }
  }
  return renderContainer();
});

export default React.memo(ResourceCover);
