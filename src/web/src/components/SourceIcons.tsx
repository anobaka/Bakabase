import React from "react";
import { FaSteam } from "react-icons/fa6";
import { RiBookOpenLine, RiRobot2Line } from "react-icons/ri";

import dlsiteLogo from "@/assets/logo/dlsite.png";
import exhentaiLogo from "@/assets/logo/exhentai.png";
import fanboxLogo from "@/assets/logo/fanbox.png";
import fantiaLogo from "@/assets/logo/fantia.png";
import cienLogo from "@/assets/logo/cien.png";
import patreonLogo from "@/assets/logo/patreon.png";
import bangumiLogo from "@/assets/logo/bangumi.png";
import pixivLogo from "@/assets/logo/pixiv.png";

export const SteamIcon = FaSteam;

export const AigcIcon = RiRobot2Line;

export const DLsiteIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="DLsite" className={`h-[1em] ${className ?? ""}`} src={dlsiteLogo} />
);

export const ExHentaiIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="ExHentai" className={`h-[1em] ${className ?? ""}`} src={exhentaiLogo} />
);

export const BangumiIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="Bangumi" className={`h-[1em] ${className ?? ""}`} src={bangumiLogo} />
);

export const PixivIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="Pixiv" className={`h-[1em] ${className ?? ""}`} src={pixivLogo} />
);

export const VndbIcon: React.FC<{ className?: string }> = ({ className }) => (
  <RiBookOpenLine aria-label="VNDB" className={className} role="img" />
);

export const FanboxIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="Fanbox" className={`h-[1em] ${className ?? ""}`} src={fanboxLogo} />
);

export const FantiaIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="Fantia" className={`h-[1em] ${className ?? ""}`} src={fantiaLogo} />
);

export const CienIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="Cien" className={`h-[1em] ${className ?? ""}`} src={cienLogo} />
);

export const PatreonIcon: React.FC<{ className?: string }> = ({ className }) => (
  <img alt="Patreon" className={`h-[1em] ${className ?? ""}`} src={patreonLogo} />
);
