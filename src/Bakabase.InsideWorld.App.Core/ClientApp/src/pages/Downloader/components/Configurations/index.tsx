import React, { useEffect, useState } from 'react';
import { Dialog, Message } from '@alifd/next';
import i18n from 'i18next';
import './index.scss';
import DownloaderOptions from './DownloaderOptions';
import {
  GetAllDownloaderNamingDefinitions,
  GetBilibiliOptions,
  GetExHentaiOptions,
  GetPixivOptions,
  PatchBilibiliOptions,
  PatchExHentaiOptions,
  PatchPixivOptions,
} from '@/sdk/apis';
import { ThirdPartyId } from '@/sdk/constants';

const StaticDownloaderOptions = [
  {
    GetApi: GetExHentaiOptions,
    PatchApi: PatchExHentaiOptions,
    thirdPartyId: ThirdPartyId.ExHentai,
    visibleKeys: ['cookie', 'threads', 'interval', 'defaultdownloadpath', 'namingconvention'],
  },
  {
    GetApi: GetPixivOptions,
    PatchApi: PatchPixivOptions,
    thirdPartyId: ThirdPartyId.Pixiv,
    visibleKeys: ['cookie', 'threads', 'interval', 'defaultdownloadpath', 'namingconvention'],
  },
  {
    GetApi: GetBilibiliOptions,
    PatchApi: PatchBilibiliOptions,
    thirdPartyId: ThirdPartyId.Bilibili,
    visibleKeys: ['cookie', 'interval', 'defaultdownloadpath', 'namingconvention'],
  },
];

export default ({
  onSaved = () => {
  },
  onClose = () => {
  },
}) => {
  const [thirdPartyIdOptionsMap, setThirdPartyIdOptionsMap] = useState({});

  const [allNamingDefinitions, setAllNamingDefinitions] = useState({});

  useEffect(() => {
    GetAllDownloaderNamingDefinitions()
      .invoke((a) => {
        setAllNamingDefinitions(a.data);
      });
  }, []);

  const downloaderOptions = StaticDownloaderOptions.map((a) => (
    {
      ...a,
      namingDefinitions: allNamingDefinitions[a.thirdPartyId],
      onChange: (options) => {
        setThirdPartyIdOptionsMap({
          ...thirdPartyIdOptionsMap,
          [a.thirdPartyId]: options,
        });
      },
    }
  ));

  // console.log(downloaderOptions, allNamingDefinitions);

  return (
    <Dialog
      visible
      closeable
      onClose={onClose}
      onCancel={onClose}
      title={i18n.t('Configurations')}
      onOk={() => {
        const allOptionsTasks = StaticDownloaderOptions.map((a) => ({
          options: thirdPartyIdOptionsMap[a.thirdPartyId],
          Api: a.PatchApi,
        }))
          .map((a) => a.Api({
            model: a.options || {},
          })
            .invoke());

        Promise.all(allOptionsTasks)
          .then((responses) => {
            if (responses.every((r) => !r.code)) {
              Message.success({
                title: i18n.t('Success'),
                align: 'cc cc',
              });
              onSaved();
            }
          });
      }}
    >
      <div className={'downloader-configurations'}>
        <div className="forms">
          {downloaderOptions.map((d) => {
            return (
              <DownloaderOptions {...d} />
            );
          })}
        </div>
      </div>
    </Dialog>
  );
};
