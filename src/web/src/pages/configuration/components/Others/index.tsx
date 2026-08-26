"use client";

import type { Key } from "@react-types/shared";
import type { BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputNetworkOptionsPatchInputModel } from "@/sdk/Api";
import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineDelete } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { Button, Input, Modal, Select, Switch } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useNetworkOptionsStore, useAppOptionsStore } from "@/stores/options";
import { OnboardingModal, useOnboarding } from "@/components/Onboarding";
import SettingsSection from "@/pages/configuration/components/SettingsSection";

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
    ...(networkOptions.customProxies?.map((c) => ({
      label: c.address!,
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

            <Button
              color="primary"
              size="sm"
              onClick={() => {
                let p: string;

                createPortal(Modal, {
                  defaultVisible: true,
                  size: "lg",
                  title: t("configuration.others.proxy.addModal.title"),
                  children: (
                    <Input
                      placeholder={t("configuration.others.proxy.tip")}
                      onValueChange={(v) => (p = v)}
                    />
                  ),
                  onOk: async () => {
                    if (p === undefined || p.length === 0) {
                      toast.error(t("common.error.invalidData"));
                      throw new Error("Invalid data");
                    }
                    await BApi.options.patchNetworkOptions({
                      customProxies: [...(networkOptions.customProxies ?? []), { address: p }],
                    });
                  },
                });
              }}
            >
              {t("common.action.add")}
            </Button>
            {networkOptions?.proxy?.mode === ProxyMode.UseCustom &&
              networkOptions?.proxy?.customProxyId && (
                <Button
                  isIconOnly
                  color="danger"
                  size="sm"
                  variant="light"
                  onClick={async () => {
                    const remaining = (networkOptions.customProxies ?? []).filter(
                      (c) => c.id !== networkOptions.proxy?.customProxyId,
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
