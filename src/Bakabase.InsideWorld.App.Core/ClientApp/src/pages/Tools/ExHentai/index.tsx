import React, { useCallback, useEffect, useRef, useState } from 'react';

import './index.scss';
import i18n from 'i18next';
import { Balloon, Button, Dialog, Icon, Input, Message, NumberPicker } from '@alifd/next';
import { GetAppOptions, OpenFileOrDirectory, PatchAppOptions, ValidateCookie } from '@/sdk/apis';
import FileSelector from '@/components/FileSelector';
import ExternalLink from '@/components/ExternalLink';
import CustomIcon from '@/components/CustomIcon';

export default () => {
  const [options, setOptions] = useState({});
  const [error, setError] = useState();
  const [imageLimits, setImageLimits] = useState();
  const [gettingImageLimits, setGettingImageLimits] = useState(false);

  const cookieChangedRef = useRef(false);

  const getImageLimits = useCallback(() => {
    setGettingImageLimits(true);
    GetExhentaiImageLimits().invoke((b) => {
      setImageLimits(b.data);
    }).finally(() => {
      setGettingImageLimits(false);
    });
  }, []);


  useEffect(() => {
    GetAppOptions().invoke((a) => {
      setOptions(a.data);
      if (a.data.exHentaiCookie) {
        getImageLimits();
      }
    });
  }, []);


  const saveOptions = () => {
    PatchAppOptions({
      model: {
        exHentaiCookie: options.exHentaiCookie,
        exHentaiDownloadPath: options.exHentaiDownloadPath,
        exHentaiLinks: options.exHentaiLinks,
        exHentaiDownloadThreads: options.exHentaiDownloadThreads,
      },
    }).invoke((a) => {
      if (!a.code) {
        Message.success(i18n.t('Success'));
        cookieChangedRef.current = false;
        getImageLimits();
      }
    });
  };

  return (
    <div className={'exhentai-downloader'}>
      <div className={'form'}>
        <div className="label">{i18n.t('Cookie')}</div>
        <div className="value">
          <Input.TextArea
            value={options.exHentaiCookie}
            onChange={(v) => {
              cookieChangedRef.current = true;
              setOptions({ ...options, exHentaiCookie: v });
            }}
          />
        </div>
        <div className="label">{i18n.t('Download path')}</div>
        <div className="value">
          <FileSelector
            type={'folder'}
            value={options.exHentaiDownloadPath}
            multiple={false}
            onChange={(v) => setOptions({ ...options, exHentaiDownloadPath: v })}
          />
        </div>
        <div className="label">
          {i18n.t('Threads')}
          <Balloon.Tooltip
            trigger={(
              <CustomIcon type={'question-circle'} />
            )}
            triggerType={'hover'}
          >
            {i18n.t('If you are browsing exhentai.org, you should decrease the threads of downloading.')}
          </Balloon.Tooltip>
        </div>
        <div className="value">
          <NumberPicker
            value={options.exHentaiDownloadThreads}
            min={1}
            max={5}
            onChange={(v) => setOptions({ ...options, exHentaiDownloadThreads: v })}
          />
        </div>
        <div className="label">{i18n.t('Links')}</div>
        <div className="value">
          <Input.TextArea
            autoHeight={{ minRows: 4 }}
            hasBorder
            placeholder={i18n.t('One link per line')}
            value={options.exHentaiLinks?.join('\n')}
            onChange={(v) => setOptions({ ...options, exHentaiLinks: v?.split('\n') })}
          />
        </div>
        <div className="label">{i18n.t('Image limits')}</div>
        <div className="value">
          {gettingImageLimits ? (
            <Icon type={'loading'} />
          ) : imageLimits ? (
            <div>
              {imageLimits.current}/{imageLimits.limit}
              &nbsp;
              <ExternalLink to={'https://e-hentai.org/home.php'}>{i18n.t('Go to reset')}</ExternalLink>
            </div>
          ) : (
            <div style={{ color: 'red' }}>
              {i18n.t('Unable to get image limits')}
            </div>
          )}
        </div>
      </div>
      <div className="opt">
        <Balloon.Tooltip
          trigger={(
            <Button
              type={'primary'}
              onClick={() => {
                if (!options.exHentaiCookie || !options.exHentaiDownloadPath) {
                  return Message.error(i18n.t('Invalid data'));
                }
                if (cookieChangedRef.current) {
                  const dialog = Dialog.notice({
                    title: i18n.t('Validating cookie'),
                    content: i18n.t('Please wait...'),
                    footer: false,
                    closeable: false,
                  });
                  ValidateCookie({
                    target: 2,
                    cookie: options.exHentaiCookie,
                  }).invoke((a) => {
                    dialog.hide();
                    if (!a.code) {
                      saveOptions();
                    }
                  });
                } else {
                  saveOptions();
                }
              }}
            >
              {i18n.t('Save')}
            </Button>
        )}
          triggerType={'hover'}
          align={'t'}
        >
          {i18n.t('Your download will start automatically if links are set')}
        </Balloon.Tooltip>
        {options.exHentaiDownloadPath && (
          <Button
            type={'normal'}
            onClick={() => {
              OpenFileOrDirectory({
                path: options.exHentaiDownloadPath,
              }).invoke();
            }}
          >
            {i18n.t('Open downloads')}
          </Button>
        )}

      </div>
      {error && (
        <div className="error">
          <div className="title">
            {i18n.t('Last error')}
          </div>
          <div className="content">{error}</div>
        </div>
      )}
    </div>
  );
};
