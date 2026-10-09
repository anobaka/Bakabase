"use client";

import type { AriaTabListProps } from "@react-aria/tabs";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Select, SelectItem, Tab, Tabs } from "@heroui/react";
import { AiOutlineVideoCamera } from "react-icons/ai";

const SELECTED_TAB_STORAGE_KEY = "thirdPartyConfig.selectedTab";
// HeroUI 2.8 styles isVertical but does not forward it to React Aria, while its
// public props omit orientation. Pass the typed Aria option through for arrow keys.
const verticalTabListProps: Pick<AriaTabListProps<object>, "orientation"> = {
  orientation: "vertical",
};

import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import {
  AvSourcesConfigPanel,
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

// Tabs that don't map to a single ThirdPartyId — render with a local icon instead.
const CUSTOM_TAB_ICONS: Record<string, React.ReactNode> = {
  avSources: <AiOutlineVideoCamera className="text-base" />,
};

function SourceIcon({ sourceKey }: { sourceKey: string }) {
  return (
    <span aria-hidden className="flex h-5 w-5 shrink-0 items-center justify-center">
      {THIRD_PARTY_TAB_KEY_TO_ID[sourceKey] !== undefined ? (
        <ThirdPartyIcon size="sm" thirdPartyId={THIRD_PARTY_TAB_KEY_TO_ID[sourceKey]} />
      ) : (
        (CUSTOM_TAB_ICONS[sourceKey] ?? null)
      )}
    </span>
  );
}

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
  const { t } = useTranslation();
  const [selectedTab, setSelectedTab] = useState<string>(() => {
    if (typeof window === "undefined") return "bilibili";

    try {
      return localStorage.getItem(SELECTED_TAB_STORAGE_KEY) || "bilibili";
    } catch {
      return "bilibili";
    }
  });
  const thirdPartySettings = useMemo(
    () => [
      {
        key: "bilibili",
        label: "Bilibili",
        tip: "thirdPartyConfig.tip.bilibili",
        content: <BilibiliConfigPanel fields="all" />,
      },
      {
        key: "exhentai",
        label: "ExHentai",
        tip: "thirdPartyConfig.tip.exhentai",
        content: <ExHentaiConfigPanel fields="all" />,
      },
      { key: "steam", label: "Steam", content: <SteamConfigPanel fields="all" /> },
      {
        key: "pixiv",
        label: "Pixiv",
        tip: "thirdPartyConfig.tip.pixiv",
        content: <PixivConfigPanel fields="all" />,
      },
      {
        key: "soulplus",
        label: "SoulPlus",
        tip: "thirdPartyConfig.tip.soulplus",
        content: <SoulPlusConfigPanel fields="all" />,
      },
      {
        key: "bangumi",
        label: "Bangumi",
        tip: "thirdPartyConfig.tip.bangumi",
        content: <BangumiConfigPanel fields="all" />,
      },
      {
        key: "cien",
        label: "Cien",
        tip: "thirdPartyConfig.tip.cien",
        content: <CienConfigPanel fields="all" />,
      },
      {
        key: "dlsite",
        label: "DLsite",
        tip: "thirdPartyConfig.tip.dlsite",
        content: <DLsiteConfigPanel fields="all" />,
      },
      {
        key: "fanbox",
        label: "Fanbox",
        tip: "thirdPartyConfig.tip.fanbox",
        content: <FanboxConfigPanel fields="all" />,
      },
      {
        key: "fantia",
        label: "Fantia",
        tip: "thirdPartyConfig.tip.fantia",
        content: <FantiaConfigPanel fields="all" />,
      },
      {
        key: "patreon",
        label: "Patreon",
        tip: "thirdPartyConfig.tip.patreon",
        content: <PatreonConfigPanel fields="all" />,
      },
      {
        key: "tmdb",
        label: "TMDB",
        tip: "thirdPartyConfig.tip.tmdb",
        content: <TmdbConfigPanel fields="all" />,
      },
      {
        key: "avSources",
        label: t("avSources.tab.label", "AV Sources"),
        content: <AvSourcesConfigPanel />,
      },
    ],
    [t],
  );
  const activeSetting =
    thirdPartySettings.find((s) => s.key === selectedTab) ?? thirdPartySettings[0];
  const selectSource = (key: string) => {
    if (!thirdPartySettings.some((s) => s.key === key)) return;
    setSelectedTab(key);
    try {
      localStorage.setItem(SELECTED_TAB_STORAGE_KEY, key);
    } catch {
      // The selection still works when browser storage is unavailable.
    }
  };

  return (
    <div className="flex min-w-0 flex-col gap-5">
      <Select
        disallowEmptySelection
        aria-label={t("thirdPartyConfig.navigation.source.label")}
        className="md:hidden"
        classNames={{ trigger: "min-h-11 bg-default-100/60 shadow-none" }}
        label={t("thirdPartyConfig.navigation.source.label")}
        labelPlacement="outside"
        renderValue={() => (
          <div className="flex items-center gap-2.5">
            <SourceIcon sourceKey={activeSetting.key} />
            <span>{activeSetting.label}</span>
          </div>
        )}
        selectedKeys={[activeSetting.key]}
        onSelectionChange={(keys) => {
          const key = Array.from(keys)[0];

          if (key !== undefined) selectSource(String(key));
        }}
      >
        {thirdPartySettings.map((s) => (
          <SelectItem
            key={s.key}
            startContent={<SourceIcon sourceKey={s.key} />}
            textValue={s.label}
          >
            {s.label}
          </SelectItem>
        ))}
      </Select>
      <Tabs
        {...verticalTabListProps}
        disableCursorAnimation
        isVertical
        aria-label={t("thirdPartyConfig.navigation.source.label")}
        classNames={{
          base: "hidden w-40 shrink-0 md:flex",
          tabList: "w-full gap-1 rounded-none bg-transparent p-0",
          tab: "h-10 justify-start rounded-lg px-3 data-[selected=true]:bg-primary/10 dark:data-[selected=true]:bg-primary/20 data-[hover-unselected=true]:bg-default-100/60 data-[hover-unselected=true]:opacity-100",
          tabContent:
            "w-full text-left text-foreground-500 group-data-[selected=true]:font-medium group-data-[selected=true]:text-primary",
          tabWrapper: "w-full min-w-0 items-start gap-6",
          panel: "min-w-0 flex-1 px-0 py-0",
        }}
        selectedKey={activeSetting.key}
        variant="light"
        onSelectionChange={(key) => selectSource(String(key))}
      >
        {thirdPartySettings.map((s) => (
          <Tab
            key={s.key}
            title={
              <div className="flex w-full items-center gap-2.5">
                <SourceIcon sourceKey={s.key} />
                <span className="truncate">{s.label}</span>
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
    </div>
  );
}
