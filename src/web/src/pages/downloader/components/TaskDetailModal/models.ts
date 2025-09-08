import type { components } from "@/sdk/BApi2";

import {
  BilibiliDownloadTaskType,
  ExHentaiDownloadTaskType,
  PixivDownloadTaskType,
  ThirdPartyId,
} from "@/sdk/constants";
import { IconType } from "react-icons";
import { AiOutlineSearch, AiOutlineStar, AiOutlineUnorderedList } from "react-icons/ai";
import { FaImages, FaMagnet, FaRankingStar } from "react-icons/fa6";

type Form =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input.DownloadTaskAddInputModel"];

export type ThirdPartyFormComponentProps<TEnumType> = {
  type: TEnumType;
  form?: Partial<Form>;
  onChange: (form: Partial<Form>) => void;
  isReadOnly?: boolean;
};

export const DownloadTaskTypeIconMap: Record<number, Record<number, IconType>> = {
  [ThirdPartyId.Bilibili]: {
    [BilibiliDownloadTaskType.Favorites]: AiOutlineStar,
  },
  [ThirdPartyId.ExHentai]: {
    [ExHentaiDownloadTaskType.List]: AiOutlineUnorderedList,
    [ExHentaiDownloadTaskType.Torrent]: FaMagnet,
    [ExHentaiDownloadTaskType.Watched]: AiOutlineStar,
    [ExHentaiDownloadTaskType.SingleWork]: FaImages,
  },
  [ThirdPartyId.Pixiv]: {
    [PixivDownloadTaskType.Following]: AiOutlineStar,
    [PixivDownloadTaskType.Ranking]: FaRankingStar,
    [PixivDownloadTaskType.Search]: AiOutlineSearch,
  }
} 

export enum DownloadTaskFieldType {
  BilibiliFavorites = 1,
  PageRange = 2,
  FfMpegRequired = 3,
  LuxRequired = 4,
  Key = 5,
  Keys = 6,
  DownloadPath = 7,
  CheckInterval = 8,
  Checkpoint = 9,
  AutoRetry = 10,
  AllowDuplicate = 11,
}

export type DownloadTaskField = {
  type: DownloadTaskFieldType;
  label?: string;
  placeholder?: string;
  defaultValue?: string;
};

export const DownloadTaskFieldMap: {
  [key in ThirdPartyId]?: Record<number, DownloadTaskField[]>;
} = {
  [ThirdPartyId.Bilibili]: {
    [BilibiliDownloadTaskType.Favorites]: [
      {
        type: DownloadTaskFieldType.FfMpegRequired,
      },
      {
        type: DownloadTaskFieldType.LuxRequired,
      },
      {
        type: DownloadTaskFieldType.BilibiliFavorites,
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.CheckInterval,
      },
      {
        type: DownloadTaskFieldType.PageRange,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
  },
  [ThirdPartyId.ExHentai]: {
    [ExHentaiDownloadTaskType.List]: [
      {
        type: DownloadTaskFieldType.Keys,
        placeholder: `https://exhentai.org/g/xxxxx/xxxxx/
https://exhentai.org/g/xxxxx/xxxxx/
...`,
        label: "Urls",
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.CheckInterval,
      },
      {
        type: DownloadTaskFieldType.PageRange,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
    [ExHentaiDownloadTaskType.Torrent]: [
      {
        type: DownloadTaskFieldType.Keys,
        placeholder: `https://exhentai.org/g/xxxxx/xxxxx/
https://exhentai.org/g/xxxxx/xxxxx/
...`,
        label: "Urls",
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
    [ExHentaiDownloadTaskType.Watched]: [
      {
        type: DownloadTaskFieldType.Key,
        defaultValue: "https://exhentai.org/watched",
        label: "Watch url",
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.CheckInterval,
      },
      {
        type: DownloadTaskFieldType.PageRange,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
    [ExHentaiDownloadTaskType.SingleWork]: [
      {
        type: DownloadTaskFieldType.Keys,
        placeholder: `https://exhentai.org/g/xxxxx/xxxxx/`,
        label: "Url",
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.CheckInterval,
      },
      {
        type: DownloadTaskFieldType.PageRange,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
  },
  [ThirdPartyId.Pixiv]: {
    [PixivDownloadTaskType.Following]: [
      {
        type: DownloadTaskFieldType.Keys,
        label: "Urls",
        placeholder: `https://www.pixiv.net/bookmark_new_illust.php
https://www.pixiv.net/bookmark_new_illust_r18.php
https://www.pixiv.net/bookmark_new_illust_r18.php?p=3`,
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.CheckInterval,
      },
      {
        type: DownloadTaskFieldType.PageRange,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
    [PixivDownloadTaskType.Ranking]: [
      {
        type: DownloadTaskFieldType.Keys,
        label: "Urls",
        placeholder: `https://www.pixiv.net/ranking.php
https://www.pixiv.net/ranking.php?mode=daily_r18`,
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.CheckInterval,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
    [PixivDownloadTaskType.Search]: [
      {
        type: DownloadTaskFieldType.Keys,
        label: "Urls",
        placeholder: `https://www.pixiv.net/tags/azurlane
https://www.pixiv.net/tags/azurlane/top
https://www.pixiv.net/tags/azurlane/illustrations
https://www.pixiv.net/tags/azurlane/manga
https://www.pixiv.net/tags/azurlane/artworks?order=popular_male_d&mode=safe`,
      },
      {
        type: DownloadTaskFieldType.DownloadPath,
      },
      {
        type: DownloadTaskFieldType.CheckInterval,
      },
      {
        type: DownloadTaskFieldType.PageRange,
      },
      {
        type: DownloadTaskFieldType.Checkpoint,
      },
      {
        type: DownloadTaskFieldType.AutoRetry,
      },
      {
        type: DownloadTaskFieldType.AllowDuplicate,
      },
    ],
  },
};
