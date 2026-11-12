"use client";

import type { AriaTabListProps } from "@react-aria/tabs";
import type { ReactNode } from "react";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { SelectItem } from "@heroui/react";
import { AiOutlineVideoCamera } from "react-icons/ai";

const SELECTED_TAB_STORAGE_KEY = "thirdPartyConfig.selectedTab";
// HeroUI 2.8 styles isVertical but does not forward it to React Aria, while its
// public props omit orientation. Pass the typed Aria option through for arrow keys.
const verticalTabListProps: Pick<AriaTabListProps<object>, "orientation"> = {
  orientation: "vertical",
};

import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { Select, Tab, Tabs } from "@/components/bakaui";
import BaiduNetdiskConfig from "@/components/ThirdPartyConfig/platforms/BaiduNetdiskConfig";
import CloudSourceConfig from "@/components/ThirdPartyConfig/platforms/CloudSourceConfig";
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
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import { DownloadMethodStatusChip } from "@/components/CloudDownload/DownloadMethodAvailability";

const THIRD_PARTY_TAB_KEY_TO_ID: Record<string, ThirdPartyId> = {
  ...Object.fromEntries(
    CloudDownloadSources.map((source) => [source.providerId, source.thirdPartyId]),
  ),
  baidu: ThirdPartyId.BaiduNetdisk,
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

type SourceSetting = {
  key: string;
  label: string;
  secondaryLabel?: string;
  tip?: string;
  content: ReactNode;
};

const sourceText = ({ label, secondaryLabel }: SourceSetting) =>
  secondaryLabel ? `${label} ${secondaryLabel}` : label;

function SourceName({
  label,
  secondaryLabel,
  providerId,
}: Pick<SourceSetting, "label" | "secondaryLabel"> & { providerId: string }) {
  return (
    <span className="flex min-w-0 flex-col items-start gap-0.5 text-left">
      <span className="max-w-full truncate text-sm leading-5">{label}</span>
      {secondaryLabel && (
        <span className="max-w-full truncate text-xs font-normal leading-4 text-default-500">
          {secondaryLabel}
        </span>
      )}
      <DownloadMethodStatusChip providerId={providerId} />
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
  const thirdPartySettings = useMemo<SourceSetting[]>(
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
      {
        key: "baidu",
        label: t(getEnumKey("ThirdPartyId", "BaiduNetdisk")),
        secondaryLabel: "BaiduPCS-Go",
        content: <BaiduNetdiskConfig />,
      },
      ...CloudDownloadSources.filter((source) => source.providerId !== "baidu").map((source) => ({
        key: source.providerId,
        label:
          source.providerId === "oneDrive"
            ? "OneDrive"
            : t(getEnumKey("ThirdPartyId", ThirdPartyId[source.thirdPartyId])),
        secondaryLabel:
          source.providerId === "oneDrive"
            ? "SharePoint"
            : source.providerId === "pikpak"
              ? "PikPak CLI · rclone"
              : source.providerId === "mega"
                ? "MEGAcmd"
                : source.providerId === "terabox"
                  ? "TeraBox CLI"
                  : undefined,
        content: <CloudSourceConfig providerId={source.providerId} />,
      })),
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
        classNames={{ trigger: "h-auto min-h-11 bg-default-100/60 py-2 shadow-none" }}
        label={t("thirdPartyConfig.navigation.source.label")}
        labelPlacement="outside"
        renderValue={() => (
          <div className="flex min-w-0 items-center gap-2.5">
            <SourceIcon sourceKey={activeSetting.key} />
            <SourceName
              label={activeSetting.label}
              secondaryLabel={activeSetting.secondaryLabel}
              providerId={activeSetting.key}
            />
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
            description={s.secondaryLabel}
            startContent={<SourceIcon sourceKey={s.key} />}
            endContent={<DownloadMethodStatusChip providerId={s.key} />}
            textValue={sourceText(s)}
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
          base: "hidden min-h-0 w-40 shrink-0 md:flex",
          tabList:
            "max-h-[calc(100dvh-2rem)] w-full gap-1 overflow-x-hidden overflow-y-auto overscroll-contain rounded-none bg-transparent p-0",
          tab: "h-auto min-h-10 shrink-0 justify-start rounded-lg px-3 py-2 data-[selected=true]:bg-primary/10 dark:data-[selected=true]:bg-primary/20 data-[hover-unselected=true]:bg-default-100/60 data-[hover-unselected=true]:opacity-100",
          tabContent:
            "w-full text-left text-foreground group-data-[selected=true]:font-medium group-data-[selected=true]:text-primary",
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
            aria-label={sourceText(s)}
            textValue={sourceText(s)}
            title={
              <div className="flex w-full items-center gap-2.5">
                <SourceIcon sourceKey={s.key} />
                <SourceName label={s.label} secondaryLabel={s.secondaryLabel} providerId={s.key} />
              </div>
            }
          >
            <div className="space-y-4">
              <ThirdPartyTabTip tipKey={s.tip} />
              {s.content}
            </div>
          </Tab>
        ))}
      </Tabs>
    </div>
  );
}
