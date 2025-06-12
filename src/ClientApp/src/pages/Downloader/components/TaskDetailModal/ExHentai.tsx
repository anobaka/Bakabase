import _ from 'lodash';
import { useTranslation } from 'react-i18next';
import React from 'react';
import type { ThirdPartyFormComponentProps } from './models';
import { Input, Textarea } from '@/components/bakaui';
import { ExHentaiDownloadTaskType } from '@/sdk/constants';
import PageRange from '@/pages/Downloader/components/TaskDetailModal/PageRange';
import OptionsBasedDownloadPathSelector
  from '@/pages/Downloader/components/TaskDetailModal/OptionsBasedDownloadPathSelector';
import store from '@/store';

type Props = ThirdPartyFormComponentProps<ExHentaiDownloadTaskType>;

export default ({
                  type,
                  form,
                  onChange,
                  isReadOnly,
                }: Props) => {
  const { t } = useTranslation();
  const knMap = form?.keyAndNames ?? {};

  const exhentaiOptions = store.useModelState('exHentaiOptions');

  const onChangeInternal = (v: typeof knMap) => {
    onChange({
      keyAndNames: v,
    });
  };

  const renderOptions = () => {
    switch (type as ExHentaiDownloadTaskType) {
      case ExHentaiDownloadTaskType.SingleWork:
      case ExHentaiDownloadTaskType.Torrent:
        return (
          <>
            <div>{t('Gallery url(s)')}</div>
            <Textarea
              // label={t('Gallery url(s)')}
              value={_.keys(knMap).join('\n')}
              placeholder={`https://exhentai.org/g/xxxxx/xxxxx/
https://exhentai.org/g/xxxxx/xxxxx/
...`}
              onValueChange={v => {
                onChangeInternal?.(_.fromPairs(v.split('\n').map(line => [line, undefined])));
              }}
              isReadOnly={isReadOnly}
            />
          </>
        );
      case ExHentaiDownloadTaskType.Watched:
        // todo: https://exhentai.org/watched
        return (
          <>
            <div>{t('Watched page url')}</div>
            <Input
              value={_.keys(knMap)[0]}
              placeholder={'https://exhentai.org/watched'}
              isReadOnly={isReadOnly}
              onValueChange={v => {
                onChangeInternal?.(_.fromPairs([[v, undefined]]));
              }}
            />
            <PageRange
              start={form?.startPage}
              end={form?.endPage}
              onChange={(s, e) => {
                onChange?.({
                  startPage: s,
                  endPage: e,
                });
              }}
            />
          </>
        );
      case ExHentaiDownloadTaskType.List:
        return (
          <>
            <div>{t('List page url')}</div>
            <Input
              // label={}
              value={_.keys(knMap)[0]}
              placeholder={'https://exhentai.org/xxxxxxx'}
              isReadOnly={isReadOnly}
              onValueChange={v => {
                onChangeInternal?.(_.fromPairs([[v, undefined]]));
              }}
            />
            <PageRange
              start={form?.startPage}
              end={form?.endPage}
              onChange={(s, e) => {
                onChange?.({
                  startPage: s,
                  endPage: e,
                });
              }}
            />
          </>
        );
      default:
        return null;
    }
  };

  return (
    <>
      {renderOptions()}
      <OptionsBasedDownloadPathSelector
        options={exhentaiOptions}
        onChange={dp => onChange({
          ...form,
          downloadPath: dp,
        })}
        downloadPath={form?.downloadPath}
      />
    </>
  );
};
