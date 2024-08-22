import React, { useEffect, useState } from 'react';
import { Dialog, Message, Tab } from '@alifd/next';
import i18n from 'i18next';
import './index.scss';
import { useTranslation } from 'react-i18next';
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
import { ThirdPartyId, thirdPartyIds } from '@/sdk/constants';

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
  const { t } = useTranslation();
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
      title={t('Configurations')}
      className={'downloader-configurations-dialog'}
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
                title: t('Success'),
                align: 'cc cc',
              });
              onSaved();
            }
          });
      }}
    >
      <div className={'downloader-configurations'}>
        <div className="forms">
          <Tab>
            {downloaderOptions.map((d, i) => {
              return (
                <Tab.Item key={i} title={thirdPartyIds.find(id => id.value == d.thirdPartyId)?.label}>
                  <DownloaderOptions {...d} />
                </Tab.Item>
              );
            })}
          </Tab>
        </div>
      </div>
    </Dialog>
  );
};
