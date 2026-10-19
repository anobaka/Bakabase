"use client";

import type { BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel } from "@/sdk/Api";
import type { ThirdPartyId } from "@/sdk/constants";

import React from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import ProxySelector from "@/components/ProxySelector";
import { ProxyCapableThirdPartyIds } from "@/sdk/constants";
import { useNetworkOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

type ProxyModel = BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel;

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

  const change = async (value: ProxyModel | undefined) => {
    const next = { ...overrides };
    if (value === undefined) delete next[String(thirdPartyId)];
    else next[String(thirdPartyId)] = value;

    await BApi.options.patchNetworkOptions({ thirdPartyProxies: next });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  return (
    <div className="flex flex-col gap-1">
      <span className="text-sm font-medium">{t<string>("thirdPartyConfig.field.proxy.label")}</span>
      <ProxySelector
        allowInherit
        className="max-w-md"
        value={entry}
        customProxies={networkOptions.customProxies}
        onChange={change}
      />
      <span className="text-xs text-foreground-400">
        {t<string>("thirdPartyConfig.field.proxy.description")}
      </span>
    </div>
  );
};

ProxyField.displayName = "ProxyField";

export default ProxyField;
