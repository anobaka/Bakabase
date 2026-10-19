import { useTranslation } from "react-i18next";

import { NumberInput, Select } from "@/components/bakaui";
import NavigateButton from "@/components/NavigateButton";
import useAutoSaveToast from "@/components/ThirdPartyConfig/base/useAutoSaveToast";
import BApi from "@/sdk/BApi";
import { useNetworkOptionsStore } from "@/stores/options";

export default function BtSettingsPanel() {
  const { t } = useTranslation();
  const options = useNetworkOptionsStore((s) => s.data);
  const save = useAutoSaveToast(BApi.options.patchNetworkOptions);
  const proxy = options?.btProxy;
  const savedProxies = options?.customProxies?.filter((p) => isSupportedBtProxy(p.address)) ?? [];
  const selected =
    proxy?.mode == null || proxy.mode === 0
      ? "direct"
      : proxy.mode === 2
        ? (proxy.customProxyId ?? "missing")
        : "missing";
  const choices = [
    { value: "direct", label: t("bt.settings.proxy.direct") },
    ...savedProxies.map((p) => ({ value: p.id!, label: p.name || p.address || p.id! })),
  ];
  const unavailable = selected !== "direct" && !savedProxies.some((p) => p.id === selected);

  if (unavailable) choices.push({ value: selected, label: t("bt.settings.proxy.unavailable") });

  return (
    <div className="space-y-4 rounded-xl border border-default-200 p-4">
      <Select
        disallowEmptySelection
        dataSource={choices}
        label={t("bt.settings.proxy.label")}
        description={t("bt.settings.proxy.description")}
        selectedKeys={[selected]}
        onSelectionChange={(keys) => {
          const key = Array.from(keys)[0];
          if (key == null || key === "missing") return;
          save({
            btProxy: key === "direct" ? { mode: 0 } : { mode: 2, customProxyId: String(key) },
          });
        }}
      />
      {unavailable && (
        <p className="text-sm text-danger" role="alert">
          {t("bt.settings.proxy.unavailableDescription")}
        </p>
      )}
      {selected !== "direct" && (
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
      url.protocol === "socks5:" &&
      !!url.hostname &&
      !!url.port &&
      !url.username &&
      !url.password &&
      Number(url.port) > 0 &&
      Number(url.port) <= 65535
    );
  } catch {
    return false;
  }
}
