import React from "react";
import { FaSteam } from "react-icons/fa6";

import dlsiteLogo from "@/assets/logo/dlsite.png";
import exhentaiLogo from "@/assets/logo/exhentai.png";

export const SteamIcon = FaSteam;

export const DLsiteIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="DLsite" className={`h-[1em] ${className ?? ""}`} src={dlsiteLogo} />
);

export const ExHentaiIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="ExHentai" className={`h-[1em] ${className ?? ""}`} src={exhentaiLogo} />
);
