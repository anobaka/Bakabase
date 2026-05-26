import React from "react";

import { ResourceSource } from "@/sdk/constants";
import { SteamIcon, DLsiteIcon, ExHentaiIcon, AigcIcon } from "@/components/SourceIcons";

interface Props {
  source: ResourceSource;
  className?: string;
}

const ResourceSourceIcon: React.FC<Props> = ({ source, className = "" }) => {
  switch (source) {
    case ResourceSource.Steam:
      return <SteamIcon className={`text-base ${className}`} />;
    case ResourceSource.DLsite:
      return <DLsiteIcon className={className} />;
    case ResourceSource.ExHentai:
      return <ExHentaiIcon className={className} />;
    case ResourceSource.Aigc:
      return <AigcIcon className={`text-base ${className}`} />;
    default:
      return null;
  }
};

export default ResourceSourceIcon;
