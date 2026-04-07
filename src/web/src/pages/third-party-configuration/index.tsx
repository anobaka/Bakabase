"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Tab, Tabs } from "@heroui/react";

import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import {
  BangumiConfigPanel,
  BilibiliConfigPanel,
  CienConfigPanel,
  DLsiteConfigPanel,
  ExHentaiConfigPanel,
  FanboxConfigPanel,
  FantiaConfigPanel,
  PatreonConfigPanel,
  PixivConfigPanel,
  SoulPlusConfigPanel,
  SteamConfigPanel,
  TmdbConfigPanel,
} from "@/components/ThirdPartyConfig";
import { ThirdPartyId } from "@/sdk/constants";

const THIRD_PARTY_TAB_KEY_TO_ID: Record<string, ThirdPartyId> = {
  bilibili: ThirdPartyId.Bilibili,
  exhentai: ThirdPartyId.ExHentai,
  steam: ThirdPartyId.Steam,
  pixiv: ThirdPartyId.Pixiv,
  soulplus: ThirdPartyId.SoulPlus,
  bangumi: ThirdPartyId.Bangumi,
  cien: ThirdPartyId.Cien,
  dlsite: ThirdPartyId.DLsite,
  fanbox: ThirdPartyId.Fanbox,
  fantia: ThirdPartyId.Fantia,
  patreon: ThirdPartyId.Patreon,
  tmdb: ThirdPartyId.Tmdb,
};

function ThirdPartyTabTip({ tipKey }: { tipKey?: string }) {
  const { t } = useTranslation();
  if (!tipKey) return null;
  const text = t<string>(tipKey);
  if (!text || text === tipKey) return null;
  return (
    <div className="flex items-start gap-2 rounded-medium bg-default-100 p-3 text-default-600">
      <span className="text-sm leading-relaxed">{text}</span>
    </div>
  );
}

export default function ThirdPartyConfigurationPage() {
  const thirdPartySettings = useMemo(
    () => [
      { key: "bilibili", label: "Bilibili", tip: "thirdPartyConfig.tip.bilibili", content: <BilibiliConfigPanel fields="all" /> },
      { key: "exhentai", label: "ExHentai", tip: "thirdPartyConfig.tip.exhentai", content: <ExHentaiConfigPanel fields="all" /> },
      { key: "steam", label: "Steam", content: <SteamConfigPanel fields="all" /> },
      { key: "pixiv", label: "Pixiv", tip: "thirdPartyConfig.tip.pixiv", content: <PixivConfigPanel fields="all" /> },
      { key: "soulplus", label: "SoulPlus", tip: "thirdPartyConfig.tip.soulplus", content: <SoulPlusConfigPanel fields="all" /> },
      { key: "bangumi", label: "Bangumi", tip: "thirdPartyConfig.tip.bangumi", content: <BangumiConfigPanel fields="all" /> },
      { key: "cien", label: "Cien", tip: "thirdPartyConfig.tip.cien", content: <CienConfigPanel fields="all" /> },
      { key: "dlsite", label: "DLsite", tip: "thirdPartyConfig.tip.dlsite", content: <DLsiteConfigPanel fields="all" /> },
      { key: "fanbox", label: "Fanbox", tip: "thirdPartyConfig.tip.fanbox", content: <FanboxConfigPanel fields="all" /> },
      { key: "fantia", label: "Fantia", tip: "thirdPartyConfig.tip.fantia", content: <FantiaConfigPanel fields="all" /> },
      { key: "patreon", label: "Patreon", tip: "thirdPartyConfig.tip.patreon", content: <PatreonConfigPanel fields="all" /> },
      { key: "tmdb", label: "TMDB", tip: "thirdPartyConfig.tip.tmdb", content: <TmdbConfigPanel fields="all" /> },
    ],
    [],
  );

  return (
    <Tabs isVertical classNames={{ panel: "flex-1 w-0" }}>
      {thirdPartySettings.map((s) => (
        <Tab
          key={s.key}
          title={
            <div className="flex items-center gap-2">
              <ThirdPartyIcon size="sm" thirdPartyId={THIRD_PARTY_TAB_KEY_TO_ID[s.key]} />
              <span>{s.label}</span>
            </div>
          }
        >
          <div className="space-y-4">
            <ThirdPartyTabTip tipKey={(s as { tip?: string }).tip} />
            {s.content}
          </div>
        </Tab>
      ))}
    </Tabs>
  );
}
