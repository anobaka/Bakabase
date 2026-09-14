"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { ThirdPartyId } from "@/sdk/constants";

import { useCallback, useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Switch } from "@heroui/react";
import { FiSettings } from "react-icons/fi";

import { Button, Modal, Spinner, Tab, Tabs } from "@/components/bakaui";
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

const ExHentaiDownloaderPanel = () => {
  const { t } = useTranslation();

  return (
    <Tabs
      destroyInactiveTabPanel
      disableAnimation
      classNames={{ base: "mb-3", tabList: "gap-4 p-0", tab: "px-1", panel: "p-0 pt-2" }}
      defaultSelectedKey={ExHentaiConfigField.Download}
      variant="underlined"
    >
      <Tab key={ExHentaiConfigField.Download} title={t("thirdPartyConfig.group.download")}>
        <ExHentaiConfigPanel fields={[ExHentaiConfigField.Download]} />
      </Tab>
      <Tab key={ExHentaiConfigField.Accounts} title={t("resourceSource.config.tab.accounts")}>
        <ExHentaiConfigPanel fields={[ExHentaiConfigField.Accounts]} />
      </Tab>
      <Tab key={ExHentaiConfigField.DataFetch} title={t("downloader.config.exHentai.requests")}>
        <ExHentaiConfigPanel fields={[ExHentaiConfigField.DataFetch]} />
      </Tab>
    </Tabs>
  );
};

/** Reuse the shared platform forms; this modal only controls their presentation. */
const platformRenderers: Record<number, () => React.ReactNode> = {
  [ThirdPartyIdEnum.ExHentai]: () => <ExHentaiDownloaderPanel />,
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

const ConfigurationsModal = ({ onDestroyed }: Props) => {
  const { t } = useTranslation();
  const panelId = useId();
  const [thirdPartyIds, setThirdPartyIds] = useState<ThirdPartyId[]>([]);
  const [selectedTab, setSelectedTab] = useState<string>(GENERAL_TAB_KEY);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);

  const loadPlatforms = useCallback(async () => {
    setLoading(true);
    setLoadFailed(false);
    try {
      const res = await BApi.downloadTask.getAllDownloaderDefinitions();
      const ids = [...new Set((res.data || []).map((d) => d.thirdPartyId))].sort((a, b) => {
        const aDev = isThirdPartyDeveloping(a) ? 1 : 0;
        const bDev = isThirdPartyDeveloping(b) ? 1 : 0;

        return aDev - bDev;
      });

      setThirdPartyIds(ids);
    } catch {
      setLoadFailed(true);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadPlatforms();
  }, [loadPlatforms]);

  const selectedPlatform =
    selectedTab === GENERAL_TAB_KEY ? undefined : (Number(selectedTab) as ThirdPartyId);
  const selectedTitle =
    selectedPlatform == null
      ? t("downloader.config.tab.general")
      : t(ThirdPartyIdEnum[selectedPlatform] || `Third Party ${selectedPlatform}`);
  const renderer = selectedPlatform == null ? undefined : platformRenderers[selectedPlatform];
  const navButtonClass = (selected: boolean) =>
    `h-10 min-w-fit justify-start gap-2.5 rounded-lg px-3 text-sm font-medium md:w-full md:min-w-0 ${
      selected ? "bg-primary/10 text-primary" : "text-default-600"
    }`;

  return (
    <Modal
      defaultVisible
      classNames={{
        base: "h-[min(85dvh,52rem)] min-h-0 overflow-hidden",
        header: "border-b border-default-100 px-5 py-4",
        body: "min-h-0 flex-1 gap-0 overflow-hidden p-0",
      }}
      footer={false}
      size="5xl"
      title={t<string>("downloader.label.configurations")}
      onDestroyed={onDestroyed}
    >
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden md:flex-row">
        <nav
          aria-label={t("downloader.config.navigation.label")}
          className="flex shrink-0 gap-1 overflow-x-auto border-b border-default-100 bg-default-50/60 p-2 md:w-48 md:flex-col md:overflow-x-hidden md:border-b-0 md:border-r md:p-3"
        >
          <Button
            aria-controls={panelId}
            aria-pressed={selectedTab === GENERAL_TAB_KEY}
            className={navButtonClass(selectedTab === GENERAL_TAB_KEY)}
            size="sm"
            startContent={<FiSettings aria-hidden className="shrink-0" size={17} />}
            variant="light"
            onPress={() => setSelectedTab(GENERAL_TAB_KEY)}
          >
            {t("downloader.config.tab.general")}
          </Button>
          <div className="mb-1 mt-4 hidden px-3 text-xs font-medium text-default-400 md:block">
            {t("downloader.config.platforms.label")}
          </div>
          {loading && (
            <div className="flex justify-center p-3">
              <Spinner size="sm" />
            </div>
          )}
          {loadFailed && (
            <div className="space-y-2 px-2 py-3 text-xs text-danger">
              <p role="alert">{t("downloader.config.platforms.loadFailed")}</p>
              <Button size="sm" variant="flat" onPress={() => void loadPlatforms()}>
                {t("downloader.config.platforms.retry")}
              </Button>
            </div>
          )}
          {thirdPartyIds.map((thirdPartyId) => {
            const selected = selectedTab === String(thirdPartyId);
            const developing = isThirdPartyDeveloping(thirdPartyId);
            const title = t(ThirdPartyIdEnum[thirdPartyId] || `Third Party ${thirdPartyId}`);

            return (
              <Button
                key={thirdPartyId}
                aria-controls={panelId}
                aria-pressed={selected}
                className={navButtonClass(selected)}
                size="sm"
                startContent={<ThirdPartyIcon thirdPartyId={thirdPartyId} />}
                variant="light"
                onPress={() => setSelectedTab(String(thirdPartyId))}
              >
                <span className="min-w-0 truncate">{title}</span>
                {developing && <DevelopingChip size="sm" />}
              </Button>
            );
          })}
        </nav>
        <section
          aria-label={selectedTitle}
          className="flex min-h-0 min-w-0 flex-1 flex-col"
          id={panelId}
        >
          <div className="shrink-0 border-b border-default-100 px-5 py-4 md:px-6">
            <div className="flex items-center gap-2.5">
              {selectedPlatform == null ? (
                <FiSettings aria-hidden className="text-default-500" size={20} />
              ) : (
                <ThirdPartyIcon thirdPartyId={selectedPlatform} />
              )}
              <h2 className="text-base font-semibold">{selectedTitle}</h2>
            </div>
            <p className="mt-1.5 text-xs leading-relaxed text-default-500">
              {t(
                selectedPlatform == null
                  ? "downloader.config.general.description"
                  : "downloader.config.platform.description",
                { platform: selectedTitle },
              )}
            </p>
          </div>
          <div
            key={selectedTab}
            className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-5 py-5 md:px-6"
          >
            {selectedPlatform == null ? (
              <GeneralPanel />
            ) : renderer ? (
              renderer()
            ) : (
              <p className="text-sm text-default-500">
                {t("downloader.config.platforms.unavailable")}
              </p>
            )}
          </div>
        </section>
      </div>
    </Modal>
  );
};

export default ConfigurationsModal;
