import ExHentai from '@/assets/exhentai.png';
import Pixiv from '@/assets/pixiv.png';
import Bilibili from '@/assets/bilibili.png';
import { ThirdPartyId } from '@/sdk/constants';

const NameIcon = {
  [ThirdPartyId.ExHentai]: ExHentai,
  [ThirdPartyId.Pixiv]: Pixiv,
  [ThirdPartyId.Bilibili]: Bilibili,

};

export default NameIcon;
