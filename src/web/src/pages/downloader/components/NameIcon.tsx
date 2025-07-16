import ExHentai from "@/assets/logo/exhentai.png";
import Pixiv from "@/assets/logo/pixiv.png";
import Bilibili from "@/assets/logo/bilibili.png";
import SoulPlus from "@/assets/logo/soulplus.png";
import { ThirdPartyId } from "@/sdk/constants";

const NameIcon = {
  [ThirdPartyId.ExHentai]: ExHentai,
  [ThirdPartyId.Pixiv]: Pixiv,
  [ThirdPartyId.Bilibili]: Bilibili,
  [ThirdPartyId.SoulPlus]: SoulPlus,
};

export default NameIcon;
