import React from "react";

import { ResourceSource } from "@/sdk/constants";
import { SteamIcon, DLsiteIcon, ExHentaiIcon } from "@/components/SourceIcons";

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
    default:
      return null;
  }
};

export default ResourceSourceIcon;
