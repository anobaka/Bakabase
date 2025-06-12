import _ from 'lodash';
import React from 'react';
import { useTranslation } from 'react-i18next';
import type { ThirdPartyFormComponentProps } from './models';
import { Input } from '@/components/bakaui';
import { PixivDownloadTaskType } from '@/sdk/constants';
import PageRange from '@/pages/Downloader/components/TaskDetailModal/PageRange';
import OptionsBasedDownloadPathSelector
  from '@/pages/Downloader/components/TaskDetailModal/OptionsBasedDownloadPathSelector';
import store from '@/store';

type KeyNameMap = Record<string, string | undefined>;

type Props = ThirdPartyFormComponentProps<PixivDownloadTaskType>;

const SamplesMap: Record<PixivDownloadTaskType, string[]> = {
  [PixivDownloadTaskType.Search]: [
    'https://www.pixiv.net/tags/azurlane',
    'https://www.pixiv.net/tags/azurlane/top',
    'https://www.pixiv.net/tags/azurlane/illustrations',
    'https://www.pixiv.net/tags/azurlane/manga',
    'https://www.pixiv.net/tags/azurlane/artworks?order=popular_male_d&mode=safe'],
  [PixivDownloadTaskType.Ranking]: [
    'https://www.pixiv.net/ranking.php',
    'https://www.pixiv.net/ranking.php?mode=daily_r18'],
  [PixivDownloadTaskType.Following]: [
    'https://www.pixiv.net/bookmark_new_illust.php',
    'https://www.pixiv.net/bookmark_new_illust_r18.php',
    'https://www.pixiv.net/bookmark_new_illust_r18.php?p=3'],
};

export default ({
                  type,
                  form,
                  onChange,
                  isReadOnly,
                }: Props) => {
  const { t } = useTranslation();

  const pixivOptions = store.useModelState('pixivOptions');

  const enumType = type as PixivDownloadTaskType;
  const samplesText = SamplesMap[enumType]?.join('\n') || '';

  const hasPageRange = enumType === PixivDownloadTaskType.Search || enumType === PixivDownloadTaskType.Following;

  const renderOptions = () => {
    return (
      <>
        <div>{t('Url')}</div>
        <Input
          value={_.keys(form?.keyAndNames ?? {})[0]}
          placeholder={`Novel is not supported now, please make sure your url likes:
${samplesText}`}
          isReadOnly={isReadOnly}
          onValueChange={v => {
            onChange?.(_.fromPairs([[v, undefined]]));
          }}
        />
        {hasPageRange && (
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
        )}
      </>
    );
  };

  return (
    <>
      {renderOptions()}
      <OptionsBasedDownloadPathSelector
        options={pixivOptions}
        onChange={dp => onChange({
          ...form,
          downloadPath: dp,
        })}
        downloadPath={form?.downloadPath}
      />
    </>
  );
};
