"use client";

import type { Key } from "@react-types/shared";
import type { BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputNetworkOptionsPatchInputModel } from "@/sdk/Api";
import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineDelete, AiOutlineEdit, AiOutlineThunderbolt } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { Button, Input, Modal, Select, Switch } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useNetworkOptionsStore, useAppOptionsStore } from "@/stores/options";
import { OnboardingModal, useOnboarding } from "@/components/Onboarding";
import SettingsSection from "@/pages/configuration/components/SettingsSection";
import ProxyTestModal from "@/pages/configuration/components/Others/ProxyTestModal";

enum ProxyMode {
  DoNotUse = 0,
  UseSystem = 1,
  UseCustom = 2,
}

interface OthersProps {
  applyPatches: <T>(
    api: (patches: T) => Promise<{ code?: number }>,
    patches: T,
    success?: (rsp: unknown) => void,
  ) => void;
  query?: string;
}

const Others: React.FC<OthersProps> = ({ applyPatches, query }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { showOnboarding, resetOnboarding, completeOnboarding } = useOnboarding();

  const appOptions = useAppOptionsStore((state) => state.data);
  const networkOptions = useNetworkOptionsStore((state) => state.data);

  const proxies = [
    {
      label: t("configuration.others.proxy.doNotUse"),
      value: ProxyMode.DoNotUse.toString(),
    },
    {
      label: t("configuration.others.proxy.useSystem"),
      value: ProxyMode.UseSystem.toString(),
    },
    // Named proxies read as "Home VPN (http://…)"; unnamed ones keep showing just the
    // address, which is all there was before names existed.
    ...(networkOptions.customProxies?.map((c) => ({
      label: c.name ? `${c.name} (${c.address})` : c.address!,
      value: c.id!,
    })) ?? []),
  ];

  let selectedProxy: Key | undefined;

  if (networkOptions?.proxy) {
    const p = networkOptions.proxy;

    if (p.mode === ProxyMode.UseCustom) {
      selectedProxy = p.customProxyId!;
    } else {
      selectedProxy = p.mode?.toString();
    }
  }

  selectedProxy ??= ProxyMode.DoNotUse.toString();

  const selectedCustomProxy =
    networkOptions?.proxy?.mode === ProxyMode.UseCustom
      ? networkOptions.customProxies?.find((c) => c.id === networkOptions.proxy?.customProxyId)
      : undefined;

  type CustomProxy = NonNullable<typeof networkOptions.customProxies>[number];

  /** Add when called with nothing, edit in place when given an existing proxy. */
  const openProxyEditor = (existing?: CustomProxy) => {
    let name = existing?.name ?? "";
    let address = existing?.address ?? "";

    createPortal(Modal, {
      defaultVisible: true,
      size: "lg",
      title: t(
        existing
          ? "configuration.others.proxy.editModal.title"
          : "configuration.others.proxy.addModal.title",
      ),
      children: (
        <div className="flex flex-col gap-3">
          <Input
            defaultValue={name}
            label={t("configuration.others.proxy.name")}
            placeholder={t("configuration.others.proxy.name.placeholder")}
            onValueChange={(v) => (name = v)}
          />
          <Input
            isRequired
            defaultValue={address}
            label={t("configuration.others.proxy.address")}
            placeholder={t("configuration.others.proxy.tip")}
            onValueChange={(v) => (address = v)}
          />
        </div>
      ),
      onOk: async () => {
        if (!address?.length) {
          toast.error(t("common.error.invalidData"));
          throw new Error("Invalid data");
        }

        const trimmedName = name.trim() || undefined;
        const existingProxies = networkOptions.customProxies ?? [];
        const customProxies = existing
          ? existingProxies.map((c) =>
              c.id === existing.id ? { ...c, name: trimmedName, address } : c,
            )
          : [...existingProxies, { name: trimmedName, address }];

        await BApi.options.patchNetworkOptions({ customProxies });
        toast.success(t("common.success.saved"));
      },
    });
  };

  const otherSettings: SettingItem[] = [
    {
      id: "proxy",
      label: t("configuration.others.proxy"),
      tip: t("configuration.others.proxy.tip"),
      keywords: ["network", "socks", "http proxy", "代理", "网络"],
      render: () => {
        return (
          <div className="flex items-center gap-2">
            <div style={{ width: 300 }}>
              <Select
                dataSource={proxies}
                multiple={false}
                selectedKeys={selectedProxy === undefined ? undefined : [selectedProxy]}
                size="sm"
                onSelectionChange={(keys) => {
                  const key = Array.from(keys)[0] as string;
                  const patches: BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputNetworkOptionsPatchInputModel =
                    {};

                  if (key === ProxyMode.DoNotUse.toString()) {
                    patches.proxy = {
                      mode: ProxyMode.DoNotUse,
                      customProxyId: undefined,
                    };
                  } else {
                    if (key === ProxyMode.UseSystem.toString()) {
                      patches.proxy = {
                        mode: ProxyMode.UseSystem,
                        customProxyId: undefined,
                      };
                    } else {
                      patches.proxy = {
                        mode: ProxyMode.UseCustom,
                        customProxyId: key,
                      };
                    }
                  }
                  BApi.options.patchNetworkOptions(patches).then((x) => {
                    if (!x.code) {
                      toast.success(t("common.success.saved"));
                    }
                  });
                }}
              />
            </div>

            <Button color="primary" size="sm" onClick={() => openProxyEditor()}>
              {t("common.action.add")}
            </Button>

            {/* Testing is offered for whatever is selected, including "do not use" —
                comparing a proxy against a direct connection is how you tell a broken
                proxy apart from a broken network. */}
            <Button
              size="sm"
              variant="flat"
              onPress={() =>
                createPortal(ProxyTestModal, {
                  customProxyId: selectedCustomProxy?.id,
                  useSystemProxy: networkOptions?.proxy?.mode === ProxyMode.UseSystem,
                  proxyLabel: selectedCustomProxy
                    ? (selectedCustomProxy.name ?? selectedCustomProxy.address!)
                    : networkOptions?.proxy?.mode === ProxyMode.UseSystem
                      ? t("configuration.others.proxy.useSystem")
                      : t("configuration.others.proxy.test.directConnection"),
                  initialPresetIds: networkOptions.selectedPresetTestSiteIds ?? undefined,
                  initialCustomSites: networkOptions.customTestSites ?? undefined,
                  onSelectionPersist: (presetSiteIds, customTestSites) => {
                    BApi.options.patchNetworkOptions({
                      selectedPresetTestSiteIds: presetSiteIds,
                      customTestSites,
                    });
                  },
                })
              }
            >
              <AiOutlineThunderbolt className="text-base" />
              {t("configuration.others.proxy.test")}
            </Button>

            {selectedCustomProxy && (
              <>
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => openProxyEditor(selectedCustomProxy)}
                >
                  <AiOutlineEdit className="text-lg" />
                </Button>
                <Button
                  isIconOnly
                  color="danger"
                  size="sm"
                  variant="light"
                  onClick={async () => {
                    const remaining = (networkOptions.customProxies ?? []).filter(
                      (c) => c.id !== selectedCustomProxy.id,
                    );

                    await BApi.options.patchNetworkOptions({
                      customProxies: remaining,
                      proxy: { mode: ProxyMode.DoNotUse, customProxyId: undefined },
                    });
                    toast.success(t("common.success.saved"));
                  }}
                >
                  <AiOutlineDelete className="text-lg" />
                </Button>
              </>
            )}
          </div>
        );
      },
    },
    {
      id: "enableTracking",
      label: t("configuration.others.enableTracking"),
      tip: t("configuration.others.enableTracking.tip"),
      keywords: ["telemetry", "analytics", "privacy", "统计", "隐私"],
      render: () => {
        return (
          <Switch
            isSelected={appOptions.enableAnonymousDataTracking}
            size="sm"
            onValueChange={(checked) => {
              applyPatches(BApi.options.patchAppOptions, { enableAnonymousDataTracking: checked });
            }}
          />
        );
      },
    },
    {
      id: "maxParallelism",
      label: t("configuration.others.maxParallelism"),
      tip: t("configuration.others.maxParallelism.tip"),
      keywords: ["concurrency", "threads", "performance", "并发", "性能"],
      render: () => {
        return (
          <Input
            className="w-24"
            min={1}
            placeholder={String(appOptions.effectiveMaxParallelism)}
            size="sm"
            type="number"
            value={
              appOptions.maxParallelism !== undefined && appOptions.maxParallelism !== null
                ? String(appOptions.maxParallelism)
                : ""
            }
            onValueChange={(v) => {
              const value = v === "" ? undefined : parseInt(v, 10);

              if (value === undefined || (value >= 1 && !isNaN(value))) {
                applyPatches(BApi.options.patchAppOptions, { maxParallelism: value });
              }
            }}
          />
        );
      },
    },
    {
      id: "onboarding",
      label: t("onboarding.viewAgain"),
      keywords: ["tutorial", "guide", "intro", "引导", "教程"],
      render: () => {
        return (
          <Button color="primary" size="sm" variant="flat" onPress={resetOnboarding}>
            {t("onboarding.viewAgain")}
          </Button>
        );
      },
    },
  ];

  return (
    <>
      <OnboardingModal visible={showOnboarding} onComplete={completeOnboarding} />
      <SettingsSection
        items={otherSettings}
        keywords={["misc", "miscellaneous", "其他"]}
        query={query}
        title={t("configuration.others.title")}
      />
    </>
  );
};

Others.displayName = "Others";

export default Others;
