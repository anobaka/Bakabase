"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { ThirdPartyId } from "@/sdk/constants";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Switch } from "@heroui/react";

import { Modal, Tab, Tabs } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { isThirdPartyDeveloping } from "@/pages/downloader/models";
import DevelopingChip from "@/components/Chips/DevelopingChip";
import { ThirdPartyId as ThirdPartyIdEnum } from "@/sdk/constants";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";
import {
  ExHentaiConfigPanel,
  ExHentaiConfigField,
  DLsiteConfigPanel,
  DLsiteConfigField,
  SteamConfigPanel,
  SteamConfigField,
  BilibiliConfigPanel,
  PixivConfigPanel,
  FanboxConfigPanel,
  FantiaConfigPanel,
  CienConfigPanel,
  PatreonConfigPanel,
  BangumiConfigPanel,
} from "@/components/ThirdPartyConfig";

type Props = {
  onSubmitted?: any;
} & DestroyableProps;

/**
 * Maps each platform to a render function that returns its ConfigPanel
 * with the appropriate fields for the downloader context.
 */
const platformRenderers: Record<number, () => React.ReactNode> = {
  [ThirdPartyIdEnum.ExHentai]: () => (
    <ExHentaiConfigPanel
      fields={[
        ExHentaiConfigField.Accounts,
        ExHentaiConfigField.DataFetch,
        ExHentaiConfigField.Download,
      ]}
    />
  ),
  [ThirdPartyIdEnum.DLsite]: () => (
    <DLsiteConfigPanel
      fields={[DLsiteConfigField.Accounts, DLsiteConfigField.DataFetch, DLsiteConfigField.Download]}
      showFooter={false}
    />
  ),
  [ThirdPartyIdEnum.Steam]: () => <SteamConfigPanel fields={[SteamConfigField.Accounts]} />,
  [ThirdPartyIdEnum.Bilibili]: () => <BilibiliConfigPanel fields="all" />,
  [ThirdPartyIdEnum.Pixiv]: () => <PixivConfigPanel fields="all" />,
  [ThirdPartyIdEnum.Fanbox]: () => <FanboxConfigPanel fields="all" />,
  [ThirdPartyIdEnum.Fantia]: () => <FantiaConfigPanel fields="all" />,
  [ThirdPartyIdEnum.Cien]: () => <CienConfigPanel fields="all" />,
  [ThirdPartyIdEnum.Patreon]: () => <PatreonConfigPanel fields="all" />,
  [ThirdPartyIdEnum.Bangumi]: () => <BangumiConfigPanel fields="all" />,
};

const GENERAL_TAB_KEY = "general";

const GeneralPanel = () => {
  const { t } = useTranslation();
  const options = useDownloaderGlobalOptionsStore((s) => s.data);
  const patch = useDownloaderGlobalOptionsStore((s) => s.patch);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-start gap-3">
        <Switch
          isSelected={options?.autoStartAfterCreation ?? false}
          onValueChange={(v) => patch({ autoStartAfterCreation: v })}
        >
          <div className="flex flex-col">
            <span className="text-sm font-medium">
              {t<string>("downloader.config.autoStartAfterCreation.label")}
            </span>
            <span className="text-xs text-default-400">
              {t<string>("downloader.config.autoStartAfterCreation.description")}
            </span>
          </div>
        </Switch>
      </div>
    </div>
  );
};

const ConfigurationsModal = ({ onSubmitted, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [thirdPartyIds, setThirdPartyIds] = useState<ThirdPartyId[]>([]);
  const [selectedTab, setSelectedTab] = useState<string>(GENERAL_TAB_KEY);

  useEffect(() => {
    BApi.downloadTask.getAllDownloaderDefinitions().then((res) => {
      const ids = [...new Set((res.data || []).map((d) => d.thirdPartyId))].sort((a, b) => {
        const aDev = isThirdPartyDeveloping(a) ? 1 : 0;
        const bDev = isThirdPartyDeveloping(b) ? 1 : 0;

        return aDev - bDev;
      });

      setThirdPartyIds(ids);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      footer={false}
      size="5xl"
      title={t<string>("downloader.label.configurations")}
      onDestroyed={onDestroyed}
    >
      <Tabs
        destroyInactiveTabPanel
        disableAnimation
        isVertical
        classNames={{ panel: "flex-1 w-0" }}
        selectedKey={selectedTab}
        onSelectionChange={(key) => setSelectedTab(key as string)}
      >
        <Tab
          key={GENERAL_TAB_KEY}
          title={
            <div className="flex items-center gap-2 justify-start">
              {t<string>("downloader.config.tab.general")}
            </div>
          }
        >
          <GeneralPanel />
        </Tab>
        {thirdPartyIds.map((thirdPartyId) => {
          const isDeveloping = isThirdPartyDeveloping(thirdPartyId);
          const thirdPartyName = ThirdPartyIdEnum[thirdPartyId] || `Third Party ${thirdPartyId}`;
          const renderer = platformRenderers[thirdPartyId];

          return (
            <Tab
              key={thirdPartyId.toString()}
              title={
                <div className="flex items-center gap-2 justify-start">
                  <ThirdPartyIcon thirdPartyId={thirdPartyId} />
                  {t<string>(thirdPartyName)}
                  {isDeveloping && <DevelopingChip size="sm" />}
                </div>
              }
            >
              {renderer?.()}
            </Tab>
          );
        })}
      </Tabs>
    </Modal>
  );
};

export default ConfigurationsModal;
