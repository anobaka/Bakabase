import React, { useEffect, useState } from 'react';
import './index.scss';
import { useTranslation } from 'react-i18next';
import _ from 'lodash';
import DownloaderOptions from './DownloaderOptions';
import { ThirdPartyId } from '@/sdk/constants';
import { Modal, Tab, Tabs } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BApi from '@/sdk/BApi';
import store from '@/store';

type ConfigurableKey = 'cookie' | 'threads' | 'interval' | 'defaultDownloadPath' | 'namingConvention';

type Props = {
  onSubmitted?: any;
} & DestroyableProps;

export default ({
                  onSubmitted,
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();

  const [exhentaOptions, exhentaiOptionsDispatcher] = store.useModel('exHentaiOptions');
  const [pixivOptions, pixivOptionsDispatcher] = store.useModel('pixivOptions');
  const [bilibiliOptions, bilibiliOptionsDispatcher] = store.useModel('bilibiliOptions');

  const [tmpOptions, setTmpOptions] = useState<{ [key in ThirdPartyId]?: any }>({});

  const thirdPartyOptionsMap: {
    [key in ThirdPartyId]?: {
      configurableKeys: ConfigurableKey[];
      put: (options: any) => Promise<void>;
      options: any;
    }
  } = {
    [ThirdPartyId.Bilibili]: {
      options: bilibiliOptions,
      put: bilibiliOptionsDispatcher.put,
      configurableKeys: ['cookie', 'interval', 'defaultDownloadPath', 'namingConvention'],
    },
    [ThirdPartyId.ExHentai]: {
      options: exhentaOptions,
      put: exhentaiOptionsDispatcher.put,
      configurableKeys: ['cookie', 'threads', 'interval', 'defaultDownloadPath', 'namingConvention'],
    },
    [ThirdPartyId.Pixiv]: {
      options: pixivOptions,
      put: pixivOptionsDispatcher.put,
      configurableKeys: ['cookie', 'threads', 'interval', 'defaultDownloadPath', 'namingConvention'],
    },
  };

  const [allNamingDefinitions, setAllNamingDefinitions] = useState({});

  useEffect(() => {
    BApi.downloadTask.getAllDownloaderNamingDefinitions().then(r => {
      setAllNamingDefinitions(r.data);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      title={t('Configurations')}
      onDestroyed={onDestroyed}
      onOk={async () => {
        const tasks = _.keys(tmpOptions).filter(x => tmpOptions[x]).map(async x => {
          const options = thirdPartyOptionsMap[x]!;
          return await options.put({
            ...options.options,
            ...tmpOptions[x],
          });
        });
        await Promise.all(tasks);
        onSubmitted?.();
      }}
    >
      <Tabs>
        {_.keys(thirdPartyOptionsMap).map((d, i) => {
          const thirdPartyId = parseInt(d, 10) as ThirdPartyId;
          const options = thirdPartyOptionsMap[thirdPartyId]!;
          return (
            <Tab key={i} title={ThirdPartyId[thirdPartyId]}>
              <DownloaderOptions
                configurableKeys={options.configurableKeys}
                options={options.options}
                thirdPartyId={thirdPartyId}
                onChange={o => {
                  setTmpOptions({
                    ...tmpOptions,
                    [thirdPartyId]: o,
                  });
                }}
                namingDefinition={allNamingDefinitions[thirdPartyId]}
              />
            </Tab>
          );
        })}
      </Tabs>
    </Modal>
  );
};
