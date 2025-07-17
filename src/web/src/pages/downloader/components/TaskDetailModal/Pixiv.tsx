"use client";

import type { ThirdPartyFormComponentProps } from "./models";

import _ from "lodash";
import { useTranslation } from "react-i18next";

import { Input } from "@/components/bakaui";
import { PixivDownloadTaskType } from "@/sdk/constants";
import PageRange from "@/pages/downloader/components/TaskDetailModal/PageRange";
import OptionsBasedDownloadPathSelector from "@/pages/downloader/components/TaskDetailModal/OptionsBasedDownloadPathSelector";
import { usePixivOptionsStore } from "@/models/options";

type KeyNameMap = Record<string, string | undefined>;

type Props = ThirdPartyFormComponentProps<PixivDownloadTaskType>;

const SamplesMap: Record<PixivDownloadTaskType, string[]> = {
  [PixivDownloadTaskType.Search]: [
    "https://www.pixiv.net/tags/azurlane",
    "https://www.pixiv.net/tags/azurlane/top",
    "https://www.pixiv.net/tags/azurlane/illustrations",
    "https://www.pixiv.net/tags/azurlane/manga",
    "https://www.pixiv.net/tags/azurlane/artworks?order=popular_male_d&mode=safe",
  ],
  [PixivDownloadTaskType.Ranking]: [
    "https://www.pixiv.net/ranking.php",
    "https://www.pixiv.net/ranking.php?mode=daily_r18",
  ],
  [PixivDownloadTaskType.Following]: [
    "https://www.pixiv.net/bookmark_new_illust.php",
    "https://www.pixiv.net/bookmark_new_illust_r18.php",
    "https://www.pixiv.net/bookmark_new_illust_r18.php?p=3",
  ],
};

export default ({ type, form, onChange, isReadOnly }: Props) => {
  const { t } = useTranslation();

  const pixivOptions = usePixivOptionsStore((state) => state.data);

  const enumType = type as PixivDownloadTaskType;
  const samplesText = SamplesMap[enumType]?.join("\n") || "";

  const hasPageRange =
    enumType === PixivDownloadTaskType.Search ||
    enumType === PixivDownloadTaskType.Following;

  const renderOptions = () => {
    return (
      <>
        <div>{t<string>("Url")}</div>
        <Input
          isReadOnly={isReadOnly}
          placeholder={`Novel is not supported now, please make sure your url likes:
${samplesText}`}
          value={_.keys(form?.keyAndNames ?? {})[0]}
          onValueChange={(v) => {
            onChange?.(_.fromPairs([[v, undefined]]));
          }}
        />
        {hasPageRange && (
          <PageRange
            end={form?.endPage}
            start={form?.startPage}
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
        downloadPath={form?.downloadPath}
        options={pixivOptions}
        onChange={(dp) =>
          onChange({
            ...form,
            downloadPath: dp,
          })
        }
      />
    </>
  );
};
