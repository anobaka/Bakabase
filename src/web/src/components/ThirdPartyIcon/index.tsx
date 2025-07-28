"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import ExHentai from "@/assets/logo/exhentai.png";
import Pixiv from "@/assets/logo/pixiv.png";
import Bilibili from "@/assets/logo/bilibili.png";
import SoulPlus from "@/assets/logo/soulplus.png";
import { ThirdPartyId } from "@/sdk/constants";

const NameIcon: { [key in ThirdPartyId]?: string } = {
  [ThirdPartyId.ExHentai]: ExHentai,
  [ThirdPartyId.Pixiv]: Pixiv,
  [ThirdPartyId.Bilibili]: Bilibili,
  [ThirdPartyId.SoulPlus]: SoulPlus,
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
  };

  const img = NameIcon[thirdPartyId];

  if (!img) {
    return <AiOutlineQuestionCircle style={style} />;
  }

  return (
    <img
      alt={t<string>(`ThirdPartyId.${ThirdPartyId[thirdPartyId]}`)}
      src={img}
      style={style}
    />
  );
};

ThirdPartyIcon.displayName = "ThirdPartyIcon";

export default ThirdPartyIcon;
