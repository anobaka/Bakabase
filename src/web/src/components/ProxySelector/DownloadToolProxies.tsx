import type { ProxyOptions, ProxyModel } from "@/components/ProxySelector";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import ProxySelector, { ProxyMode } from "@/components/ProxySelector";
import BApi from "@/sdk/BApi";
import { DownloadToolProxyTargets } from "@/sdk/constants";
import { useNetworkOptionsStore } from "@/stores/options";

export function supportsToolProxy(proxy: ProxyOptions, schemes: readonly string[]): boolean {
  try {
    const address = new URL(proxy.address ?? "");

    return !!address.hostname && schemes.includes(address.protocol.slice(0, -1).toLowerCase());
  } catch {
    return false;
  }
}

/** Tool overrides are independent of site and BitTorrent settings. An absent entry inherits. */
export default function DownloadToolProxies({ toolIds }: { toolIds?: readonly string[] } = {}) {
  const { t } = useTranslation();
  const options = useNetworkOptionsStore((state) => state.data);
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);
  const savingRef = useRef(false);

  const change = async (toolId: string, value: ProxyModel | undefined) => {
    // The dictionary is replaced as a unit by the options API. Serialize this panel's saves
    // and read current state so changing another tool never drops a confirmed override.
    if (savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    setFailed(false);
    try {
      const state = useNetworkOptionsStore.getState();
      const downloadToolProxies = { ...state.data.downloadToolProxies };

      if (value === undefined) delete downloadToolProxies[toolId];
      else downloadToolProxies[toolId] = value;
      const response = await BApi.options.patchNetworkOptions({ downloadToolProxies });

      if (response.code !== 0) throw new Error("Download tool proxy save failed");
      state.update({ downloadToolProxies });
      toast.success(t("common.success.saved"));
    } catch {
      setFailed(true);
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <div className="flex min-w-0 flex-col gap-4">
      <p className="text-xs leading-relaxed text-default-500">
        {t("configuration.downloadTools.proxy.description")}
      </p>
      {DownloadToolProxyTargets.filter((target) => !toolIds || toolIds.includes(target.id)).map(
        (target) => {
          const nameKey = `configuration.downloadTools.name.${target.id}`;
          const translatedName = t(nameKey, {
            defaultValue: target.name,
          });
          const name = translatedName === nameKey ? target.name : translatedName;
          const value = options.downloadToolProxies?.[target.id] ?? undefined;
          const schemes = target.supportedProxySchemes;
          const configuredProxy = options.customProxies?.find(
            (proxy) => proxy.id === value?.customProxyId,
          );
          const unavailable =
            value?.mode === ProxyMode.UseCustom &&
            (!configuredProxy || !supportsToolProxy(configuredProxy, schemes));

          return (
            <div key={target.id} className="min-w-0 space-y-1">
              <ProxySelector
                allowInherit
                inheritLabel={t("configuration.downloadTools.proxy.inherit")}
                inheritDescription={t("configuration.downloadTools.proxy.inheritDescription")}
                label={name}
                description={t("configuration.downloadTools.proxy.protocols", {
                  protocols: schemes.join(", "),
                })}
                className="w-full min-w-0 sm:max-w-md"
                value={value}
                customProxies={options.customProxies}
                isCustomProxySupported={(proxy) => supportsToolProxy(proxy, schemes)}
                unsupportedDescription={t("configuration.downloadTools.proxy.unsupported")}
                isDisabled={saving}
                onChange={(selection) => void change(target.id, selection)}
              />
              {unavailable && (
                <p className="text-xs text-danger" role="alert">
                  {t("configuration.downloadTools.proxy.unavailable", { name })}
                </p>
              )}
            </div>
          );
        },
      )}
      {failed && (
        <p className="text-sm text-danger" role="alert">
          {t("configuration.downloadTools.proxy.saveFailed")}
        </p>
      )}
    </div>
  );
}
