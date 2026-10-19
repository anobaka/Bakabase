import { useTranslation } from "react-i18next";

import { NumberInput } from "@/components/bakaui";
import ProxySelector, { ProxyMode } from "@/components/ProxySelector";
import NavigateButton from "@/components/NavigateButton";
import useAutoSaveToast from "@/components/ThirdPartyConfig/base/useAutoSaveToast";
import BApi from "@/sdk/BApi";
import { useNetworkOptionsStore } from "@/stores/options";

export default function BtSettingsPanel() {
  const { t } = useTranslation();
  const options = useNetworkOptionsStore((s) => s.data);
  const save = useAutoSaveToast(BApi.options.patchNetworkOptions);
  const proxy = options?.btProxy;
  const unavailable =
    proxy?.mode === ProxyMode.UseCustom &&
    !(options?.customProxies ?? []).some(
      (saved) => saved.id === proxy.customProxyId && isSupportedBtProxy(saved.address),
    );

  return (
    <div className="space-y-4 rounded-xl border border-default-200 p-4">
      <ProxySelector
        value={proxy}
        customProxies={options?.customProxies}
        label={t("bt.settings.proxy.label")}
        description={t("bt.settings.proxy.description")}
        isCustomProxySupported={(saved) => isSupportedBtProxy(saved.address)}
        unsupportedDescription={t("bt.settings.proxy.unsupported")}
        onChange={(value) => value && save({ btProxy: value })}
      />
      {unavailable && (
        <p className="text-sm text-danger" role="alert">
          {t("bt.settings.proxy.unavailableDescription")}
        </p>
      )}
      {proxy?.mode != null && proxy.mode !== ProxyMode.DoNotUse && (
        <p className="text-xs text-warning-600">{t("bt.settings.proxy.udpDisabled")}</p>
      )}
      <NavigateButton to="/configuration">{t("bt.settings.proxy.manage")}</NavigateButton>
      <NumberInput
        label={t("bt.settings.concurrency.label")}
        description={t("bt.settings.concurrency.description")}
        minValue={1}
        maxValue={20}
        value={options?.btMaxConcurrentDownloads ?? 2}
        onValueChange={(value) => {
          if (Number.isInteger(value) && value >= 1 && value <= 20)
            save({ btMaxConcurrentDownloads: value });
        }}
      />
      <p className="text-xs text-default-500">{t("bt.settings.nextDownloads")}</p>
    </div>
  );
}

function isSupportedBtProxy(address?: string | null): boolean {
  try {
    const url = new URL(address ?? "");
    return (
      ["http:", "socks5:"].includes(url.protocol) &&
      !!url.hostname &&
      !url.username &&
      !url.password &&
      Number(url.port || (url.protocol === "http:" ? "80" : "0")) > 0 &&
      Number(url.port || (url.protocol === "http:" ? "80" : "0")) <= 65535
    );
  } catch {
    return false;
  }
}
