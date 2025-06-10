import {
  BilibiliDownloadTaskType,
  ExHentaiDownloadTaskType,
  PixivDownloadTaskType,
  ThirdPartyId,
} from '@/sdk/constants';

type Options = {
  type: number;
  name: string;
  hasPageRange?: boolean;
};

export const DownloadTaskOptionsMap: {[key in ThirdPartyId]?: Options[]} = {
  [ThirdPartyId.Bilibili]: [{
    type: BilibiliDownloadTaskType.Favorites,
    name: BilibiliDownloadTaskType[BilibiliDownloadTaskType.Favorites],
    hasPageRange: true,
  }],
  [ThirdPartyId.ExHentai]: [
    ...[ExHentaiDownloadTaskType.List, ExHentaiDownloadTaskType.Watched].map(),
  ],
  [ThirdPartyId.Pixiv]: [PixivDownloadTaskType.Search, PixivDownloadTaskType.Following],
};
