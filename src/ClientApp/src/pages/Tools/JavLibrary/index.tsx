import React, { useEffect, useState } from 'react';
import { Button, Input, Message, Table } from '@alifd/next';
import i18n from 'i18next';
import { OpenFileOrDirectory, PatchJavLibraryOptions } from '@/sdk/apis';
import './index.scss';
import { useProgressorHubConnection } from '@/components/ProgressorHubConnection';
import { ProgressorStatus } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import Title from '@/components/Title';
import FileSelector from '@/components/FileSelector';
import store from '@/store';

export default () => {
  const javOptions = store.useModelState('javLibraryOptions');

  const [options, setOptions] = useState(javOptions || {});
  const [state, setState] = useState({});
  const [progress, setProgress] = useState({});

  const downloaderRef = useProgressorHubConnection('JavLibraryDownloader', (progress) => setProgress(progress), (state) => setState(state));

  const downloading = state.status == ProgressorStatus.Running;

  // state.status = ProgressorStatus.Complete;
  // progress.results = {
  //   aaaa: false,
  //   bbbb: true,
  //   cccc: false,
  // };

  useEffect(() => {
    setOptions({
      ...(javOptions ?? {}),
      ...(javOptions?.collector ?? {}),
    });
  }, [javOptions]);

  useEffect(() => {

  }, []);

  const formItems = [
    {
      label: 'Download path',
      type: 'folder',
      key: 'path',
    },
    {
      label: 'Cookie',
      type: 'string',
    },
    {
      label: 'Urls',
      type: 'array',
    },
    {
      label: 'Torrent link keywords',
      key: 'TorrentLinkKeywords',
      type: 'array',
      example: 'dacload.com',
    },
  ];


  const getStartModel = () => {
    return {
      downloadPath: options.collector?.path,
      cookie: options.cookie,
      urls: options.collector?.urls,
      torrentLinkKeywords: options.collector?.torrentOrLinkKeywords,
    };
  };

  const start = () => {
    const model = getStartModel();
    if (Object.keys(model)
      .some((t) => {
        const v = model[t];
        return !v || (Array.isArray(v) && v.length == 0);
      })) {
      return Message.error(i18n.t('Invalid data'));
    }

    PatchJavLibraryOptions({
      model: options,
    }).invoke((a) => {
      if (!a.code) {
        downloaderRef.current.start(model);
      }
    });
  };

  // console.log(state, progress);

  return (
    <div className={'jav-library-page'}>
      <Title title={'JavLibrary torrents and covers downloader'} />
      <div className="form">
        <div className="item">
          <div className="label">
            {i18n.t('Download path')}
          </div>
          <div className="value folder">
            <FileSelector
              multiple={false}
              type={'folder'}
              value={options.collector?.path}
              onChange={(v) => {
                setOptions({
                  ...(options ?? {}),
                  collector: {
                    ...(options?.collector ?? {}),
                    path: v,
                  },
                });
              }}
            />
          </div>
        </div>
        <div className="item">
          <div className="label">
            {i18n.t('Cookie')}
          </div>
          <div className="value">
            <Input.TextArea
              autoHeight
              value={options.cookie}
              onChange={(v) => {
                setOptions({
                  ...(options ?? {}),
                  cookie: v,
                });
              }}
            />
          </div>
        </div>
        <div className="item">
          <div className="label">
            {i18n.t('Urls')}
          </div>
          <div className="value">
            <Input.TextArea
              placeholder={`${i18n.t('One line per item')}`}
              value={options?.collector?.urls?.join('\n')}
              onChange={(v) => {
                setOptions({
                  ...(options ?? {}),
                  collector: {
                    ...(options?.collector ?? {}),
                    urls: v?.replace('\r', '').split('\n'),
                  },
                });
              }}
              multiple
            />
          </div>
        </div>
        <div className="item">
          <div className="label">
            {i18n.t('Torrent link keywords')}
          </div>
          <div className="value">
            <Input.TextArea
              placeholder={`${i18n.t('One line per item')}. ${i18n.t('For example')}: dacload.com`}
              value={options?.collector?.torrentOrLinkKeywords?.join('\n')}
              onChange={(v) => {
                setOptions({
                  ...(options ?? {}),
                  collector: {
                    ...(options?.collector ?? {}),
                    torrentOrLinkKeywords: v?.replace('\r', '').split('\n'),
                  },
                });
              }}
              multiple
            />
          </div>
        </div>
        <div className="opt">
          <Button
            type={'normal'}
            size={'large'}
            onClick={() => {
              PatchJavLibraryOptions({
                model: options,
              }).invoke((a) => {
                if (!a.code) {
                  Message.success(i18n.t('Saved'));
                }
              });
            }}
          >{i18n.t('Save form only')}
          </Button>
          {downloading ? (
            <Button
              type={'primary'}
              warning
              size={'large'}
              onClick={() => {
                downloaderRef.current.stop();
              }}
            >{i18n.t('Stop')}
            </Button>
          ) : (
            <Button type={'primary'} size={'large'} onClick={start}>{i18n.t('Download')}</Button>
          )}
          <Button
            type={'normal'}
            size={'large'}
            onClick={() => {
              OpenFileOrDirectory({ path: options.javLibraryDownloadPath }).invoke();
            }}
          >{i18n.t('Open directory')}
          </Button>
        </div>
      </div>
      {state.status == ProgressorStatus.Suspended && (
        <div className="error">{state.message}</div>
      )}
      {state.status == ProgressorStatus.Complete && (
        <div className="error">
          {i18n.t('Failed to extract: ')}
          {Object.keys(progress?.results).filter((a) => !progress.results[a]).map((a) => <div>{a}</div>)}
        </div>
      )}
      <div className="process">
        {progress?.results && (
          <Table dataSource={Object.keys(progress.results || {}).map((t) => ({ url: t, done: progress.results[t] }))}>
            <Table.Column title={'Url'} width={'70%'} dataIndex={'url'} />
            <Table.Column
              title={'Status'}
              dataIndex={'done'}
              cell={(d) => {
                if (d === true) {
                  return (<CustomIcon type={'check-circle'} style={{ color: 'green' }} />);
                }
                if (d === false) {
                  return (<CustomIcon type={'close-circle'} style={{ color: 'red' }} />);
                }
              }}
            />
          </Table>
          )}
      </div>
    </div>
  );
};
