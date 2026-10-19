"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { ThirdPartyId } from "@/sdk/constants";

import { useCallback, useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Switch } from "@heroui/react";
import { FiGlobe, FiSettings } from "react-icons/fi";
import { TbMagnet } from "react-icons/tb";

import { Button, Modal, Spinner, Tab, Tabs } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { isThirdPartyDeveloping } from "@/pages/downloader/models";
import DevelopingChip from "@/components/Chips/DevelopingChip";
import { ThirdPartyId as ThirdPartyIdEnum } from "@/sdk/constants";
import { useDownloaderGlobalOptionsStore, useNetworkOptionsStore } from "@/stores/options";
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
import { useAutoSaveToast } from "@/components/ThirdPartyConfig/base/useAutoSaveToast";
import NavigateButton from "@/components/NavigateButton";
import { ProxyMode } from "@/components/ProxySelector";
import BtSettingsPanel from "@/pages/bt-downloads/BtSettingsPanel";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";

type Props = {
  onSubmitted?: any;
} & DestroyableProps;

const ExHentaiDownloaderPanel = () => {
  const { t } = useTranslation();

  return (
    <Tabs
      destroyInactiveTabPanel
      disableAnimation
      classNames={{ base: "mb-5 max-w-full", tabList: "gap-4 p-0", tab: "px-1", panel: "p-0 pt-2" }}
      defaultSelectedKey={ExHentaiConfigField.Download}
      variant="underlined"
    >
      <Tab key={ExHentaiConfigField.Download} title={t("thirdPartyConfig.group.download")}>
        <ExHentaiConfigPanel fields={[ExHentaiConfigField.Download]} />
      </Tab>
      <Tab
        key={ExHentaiConfigField.OriginalImages}
        title={t("downloader.config.exHentai.originalImages")}
      >
        <ExHentaiConfigPanel fields={[ExHentaiConfigField.OriginalImages]} />
      </Tab>
      <Tab
        key={ExHentaiConfigField.PostProcessing}
        title={t("downloader.config.exHentai.postProcessing")}
      >
        <ExHentaiConfigPanel fields={[ExHentaiConfigField.PostProcessing]} />
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
const BT_TAB_KEY = "bt";
const HTTP_TAB_KEY = "http";

const GeneralPanel = () => {
  const { t } = useTranslation();
  const options = useDownloaderGlobalOptionsStore((s) => s.data);
  const saveOptions = useAutoSaveToast(BApi.options.patchDownloaderGlobalOptions);

  return (
    <div className="flex flex-col gap-4">
      <DownloadDirectoryField
        description={t("downloader.config.defaultDownloadPath.description")}
        label={t("downloader.config.defaultDownloadPath.label")}
        value={options?.defaultDownloadPath}
        onChange={(path) => saveOptions({ defaultDownloadPath: path })}
      />
      <div className="flex items-start gap-3">
        <Switch
          isSelected={options?.autoStartAfterCreation ?? false}
          onValueChange={(v) => saveOptions({ autoStartAfterCreation: v })}
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

const HttpSettingsPanel = () => {
  const { t } = useTranslation();
  const options = useNetworkOptionsStore((state) => state.data);
  const proxy = options?.proxy;
  const custom = options?.customProxies?.find((item) => item.id === proxy?.customProxyId);
  const proxyLabel =
    proxy?.mode === ProxyMode.UseCustom
      ? custom?.name || custom?.address || t("configuration.others.proxy.unavailable")
      : t(
          proxy?.mode === ProxyMode.UseSystem
            ? "configuration.others.proxy.useSystem"
            : "configuration.others.proxy.doNotUse",
        );
  return (
    <div className="space-y-4">
      <div>
        <div className="text-xs text-default-500">{t("downloader.config.http.proxy")}</div>
        <div className="mt-1 text-sm">{proxyLabel}</div>
      </div>
      <NavigateButton to="/configuration">
        {t("downloader.config.http.manageNetwork")}
      </NavigateButton>
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

  const selectedPlatform = [GENERAL_TAB_KEY, BT_TAB_KEY, HTTP_TAB_KEY].includes(selectedTab)
    ? undefined
    : (Number(selectedTab) as ThirdPartyId);
  const selectedTitle =
    selectedTab === GENERAL_TAB_KEY
      ? t("downloader.config.tab.general")
      : selectedTab === BT_TAB_KEY
        ? t("downloadCenter.provider.bt")
        : selectedTab === HTTP_TAB_KEY
          ? t("downloader.config.http.title")
          : t(ThirdPartyIdEnum[selectedPlatform!] || `Third Party ${selectedPlatform}`);
  const renderer = selectedPlatform == null ? undefined : platformRenderers[selectedPlatform];
  const navButtonClass = (selected: boolean) =>
    `h-10 min-w-fit shrink-0 justify-start gap-2.5 rounded-lg px-3 text-sm font-medium md:w-full md:min-w-0 ${
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
          className="flex shrink-0 gap-1 overflow-x-auto border-b border-default-100 bg-default-50/60 p-2 md:w-48 md:min-h-0 md:flex-col md:overflow-x-hidden md:overflow-y-auto md:border-b-0 md:border-r md:p-3"
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
          <div
            className="flex shrink-0 items-center gap-1 md:flex-col md:items-stretch"
            role="group"
            aria-labelledby={`${panelId}-general-downloads`}
          >
            <div
              id={`${panelId}-general-downloads`}
              className="px-3 text-xs font-medium text-default-400 md:mb-1 md:mt-4"
            >
              {t("downloader.config.generalDownloads.label")}
            </div>
            <Button
              aria-controls={panelId}
              aria-pressed={selectedTab === BT_TAB_KEY}
              className={navButtonClass(selectedTab === BT_TAB_KEY)}
              size="sm"
              startContent={<TbMagnet aria-hidden className="shrink-0" size={17} />}
              variant="light"
              onPress={() => setSelectedTab(BT_TAB_KEY)}
            >
              {t("downloadCenter.provider.bt")}
            </Button>
            <Button
              aria-controls={panelId}
              aria-pressed={selectedTab === HTTP_TAB_KEY}
              className={navButtonClass(selectedTab === HTTP_TAB_KEY)}
              size="sm"
              startContent={<FiGlobe aria-hidden className="shrink-0" size={17} />}
              variant="light"
              onPress={() => setSelectedTab(HTTP_TAB_KEY)}
            >
              {t("downloader.config.http.title")}
            </Button>
          </div>
          <div
            className="flex shrink-0 items-center gap-1 md:flex-col md:items-stretch"
            role="group"
            aria-labelledby={`${panelId}-sites`}
          >
            <div
              id={`${panelId}-sites`}
              className="px-3 text-xs font-medium text-default-400 md:mb-1 md:mt-4"
            >
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
          </div>
        </nav>
        <section
          aria-label={selectedTitle}
          className="flex min-h-0 min-w-0 flex-1 flex-col"
          id={panelId}
        >
          <div className="shrink-0 border-b border-default-100 px-5 py-4 md:px-6">
            <div className="flex items-center gap-2.5">
              {selectedTab === BT_TAB_KEY ? (
                <TbMagnet aria-hidden className="text-default-500" size={20} />
              ) : selectedTab === HTTP_TAB_KEY ? (
                <FiGlobe aria-hidden className="text-default-500" size={20} />
              ) : selectedPlatform == null ? (
                <FiSettings aria-hidden className="text-default-500" size={20} />
              ) : (
                <ThirdPartyIcon thirdPartyId={selectedPlatform} />
              )}
              <h2 className="text-base font-semibold">{selectedTitle}</h2>
            </div>
            <p className="mt-1.5 text-xs leading-relaxed text-default-500">
              {t(
                selectedTab === GENERAL_TAB_KEY
                  ? "downloader.config.general.description"
                  : selectedTab === BT_TAB_KEY
                    ? "downloader.config.bt.description"
                    : selectedTab === HTTP_TAB_KEY
                      ? "downloader.config.http.description"
                      : "downloader.config.platform.description",
                { platform: selectedTitle },
              )}
            </p>
          </div>
          <div
            key={selectedTab}
            className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-5 py-5 md:px-6"
          >
            {selectedTab === GENERAL_TAB_KEY ? (
              <GeneralPanel />
            ) : selectedTab === BT_TAB_KEY ? (
              <BtSettingsPanel appearance="plain" />
            ) : selectedTab === HTTP_TAB_KEY ? (
              <HttpSettingsPanel />
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
