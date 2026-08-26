"use client";

import type { BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel } from "@/sdk/Api";
import type { ThirdPartyId } from "@/sdk/constants";

import React from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { Select } from "@/components/bakaui";
import { ProxyCapableThirdPartyIds } from "@/sdk/constants";
import { useNetworkOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

type ProxyModel = BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel;

/** Mirrors NetworkOptions.ProxyMode. */
const ProxyMode = {
  DoNotUse: 0,
  UseSystem: 1,
  UseCustom: 2,
} as const;

/** Not a backend mode — the UI value for "no entry", i.e. follow the global proxy. */
const INHERIT = "inherit";

interface Props {
  thirdPartyId: ThirdPartyId;
}

/**
 * Per-source proxy selector, shown alongside that source's own settings.
 *
 * Renders nothing for a source without its own HTTP client: the override is bound to that
 * handler, so offering the choice elsewhere would offer one that silently does nothing.
 */
const ProxyField: React.FC<Props> = ({ thirdPartyId }) => {
  const { t } = useTranslation();
  const networkOptions = useNetworkOptionsStore((s) => s.data);

  if (!ProxyCapableThirdPartyIds.includes(thirdPartyId)) {
    return null;
  }

  const overrides = (networkOptions.thirdPartyProxies ?? {}) as Record<string, ProxyModel>;
  const entry = overrides[String(thirdPartyId)];

  const selected = !entry
    ? INHERIT
    : entry.mode === ProxyMode.UseCustom
      ? `custom:${entry.customProxyId}`
      : String(entry.mode);

  const options = [
    { label: t("thirdPartyConfig.field.proxy.inherit"), value: INHERIT },
    { label: t("configuration.others.proxy.doNotUse"), value: String(ProxyMode.DoNotUse) },
    { label: t("configuration.others.proxy.useSystem"), value: String(ProxyMode.UseSystem) },
    ...(networkOptions.customProxies ?? []).map((c) => ({
      label: c.name ? `${c.name} (${c.address})` : (c.address ?? ""),
      value: `custom:${c.id}`,
    })),
  ];

  const change = async (value: string) => {
    const next = { ...overrides };

    // Inheriting carries no information, so drop the entry rather than storing a mode that
    // means "same as global".
    if (value === INHERIT) {
      delete next[String(thirdPartyId)];
    } else {
      next[String(thirdPartyId)] = value.startsWith("custom:")
        ? { mode: ProxyMode.UseCustom, customProxyId: value.slice("custom:".length) }
        : { mode: Number(value) as ProxyModel["mode"], customProxyId: undefined };
    }

    await BApi.options.patchNetworkOptions({ thirdPartyProxies: next });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  return (
    <div className="flex flex-col gap-1">
      <span className="text-sm font-medium">{t<string>("thirdPartyConfig.field.proxy.label")}</span>
      <Select
        className="max-w-md"
        dataSource={options}
        multiple={false}
        selectedKeys={[selected]}
        size="sm"
        onSelectionChange={(keys) => {
          const key = Array.from(keys)[0] as string;

          if (key) change(key);
        }}
      />
      <span className="text-xs text-foreground-400">
        {t<string>("thirdPartyConfig.field.proxy.description")}
      </span>
    </div>
  );
};

ProxyField.displayName = "ProxyField";

export default ProxyField;
