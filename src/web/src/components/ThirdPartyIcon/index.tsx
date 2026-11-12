"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineCloudDownload, AiOutlineQuestionCircle } from "react-icons/ai";
import { SiMega } from "react-icons/si";

import ExHentai from "@/assets/logo/exhentai.png";
import Pixiv from "@/assets/logo/pixiv.png";
import Bilibili from "@/assets/logo/bilibili.png";
import SoulPlus from "@/assets/logo/soulplus.png";
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import dlsiteLogo from "@/assets/logo/dlsite.png";
import fanboxLogo from "@/assets/logo/fanbox.png";
import fantiaLogo from "@/assets/logo/fantia.png";
import cienLogo from "@/assets/logo/cien.png";
import patreonLogo from "@/assets/logo/patreon.png";
import bangumiLogo from "@/assets/logo/bangumi.png";
import steamLogo from "@/assets/logo/steam.png";
import tmdbLogo from "@/assets/logo/tmdb.png";
import baiduNetdiskLogo from "@/assets/logo/baidu-netdisk.ico";
import googleDriveLogo from "@/assets/logo/google-drive.svg";
import gofileLogo from "@/assets/logo/gofile.png";
import pikpakLogo from "@/assets/logo/pikpak.png";
import pixeldrainLogo from "@/assets/logo/pixeldrain.png";
import oneDriveLogo from "@/assets/logo/onedrive.svg";
import teraBoxLogo from "@/assets/logo/terabox.png";

const NameIcon: { [key in ThirdPartyId]?: string } = {
  [ThirdPartyId.ExHentai]: ExHentai,
  [ThirdPartyId.Pixiv]: Pixiv,
  [ThirdPartyId.Bilibili]: Bilibili,
  [ThirdPartyId.Bangumi]: bangumiLogo,
  [ThirdPartyId.SoulPlus]: SoulPlus,
  [ThirdPartyId.DLsite]: dlsiteLogo,
  [ThirdPartyId.Fanbox]: fanboxLogo,
  [ThirdPartyId.Fantia]: fantiaLogo,
  [ThirdPartyId.Cien]: cienLogo,
  [ThirdPartyId.Patreon]: patreonLogo,
  [ThirdPartyId.Tmdb]: tmdbLogo,
  [ThirdPartyId.Steam]: steamLogo,
  [ThirdPartyId.BaiduNetdisk]: baiduNetdiskLogo,
  [ThirdPartyId.GoogleDrive]: googleDriveLogo,
  [ThirdPartyId.Gofile]: gofileLogo,
  [ThirdPartyId.PikPak]: pikpakLogo,
  [ThirdPartyId.Pixeldrain]: pixeldrainLogo,
  [ThirdPartyId.OneDrive]: oneDriveLogo,
  [ThirdPartyId.TeraBox]: teraBoxLogo,
};

type Props = {
  thirdPartyId: ThirdPartyId;
  size?: "sm" | "md" | "lg";
};
const ThirdPartyIcon = ({ thirdPartyId, size }: Props) => {
  const { t } = useTranslation();

  let sizeValue = 18;

  if (size) {
    switch (size) {
      case "sm":
        sizeValue = 18;
        break;
      case "md":
        sizeValue = 24;
        break;
      case "lg":
        sizeValue = 32;
        break;
    }
  }

  const style = {
    width: sizeValue,
    height: sizeValue,
    maxWidth: sizeValue,
    maxHeight: sizeValue,
    objectFit: "contain" as const,
  };

  const img = NameIcon[thirdPartyId];

  if (thirdPartyId === ThirdPartyId.Mega)
    return <SiMega style={style} className="text-[#d9272e]" />;

  if (!img) {
    if (CloudDownloadSources.some((source) => source.thirdPartyId === thirdPartyId)) {
      return <AiOutlineCloudDownload style={style} />;
    }
    return <AiOutlineQuestionCircle style={style} />;
  }

  return (
    <img
      alt={t<string>(getEnumKey("ThirdPartyId", ThirdPartyId[thirdPartyId]))}
      src={img}
      style={style}
    />
  );
};

ThirdPartyIcon.displayName = "ThirdPartyIcon";

export default ThirdPartyIcon;
