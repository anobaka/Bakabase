"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import NameIcon from "@/pages/downloader/components/NameIcon";
import { ThirdPartyId } from "@/sdk/constants";

type Props = {
  thirdPartyId: ThirdPartyId;
  size?: "sm" | "md" | "lg";
};

export default ({ thirdPartyId, size }: Props) => {
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
