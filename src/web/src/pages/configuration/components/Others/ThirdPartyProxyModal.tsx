"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel } from "@/sdk/Api";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Select } from "@/components/bakaui";
import { ProxyCapableThirdPartyIds, ThirdPartyId } from "@/sdk/constants";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import BApi from "@/sdk/BApi";

type ProxyModel = BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel;

/** Mirrors NetworkOptions.ProxyMode. */
const ProxyMode = {
  DoNotUse: 0,
  UseSystem: 1,
  UseCustom: 2,
} as const;

/** Not a backend mode — the UI value meaning "no entry", i.e. follow the global proxy. */
const INHERIT = "inherit";

type Props = {
  customProxies: { id?: string; name?: string | null; address?: string }[];
  thirdPartyProxies?: Record<string, ProxyModel> | null;
} & DestroyableProps;

const ThirdPartyProxyModal = ({ customProxies, thirdPartyProxies, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [overrides, setOverrides] = useState<Record<string, ProxyModel>>({
    ...(thirdPartyProxies ?? {}),
  });

  const options = [
    { label: t("configuration.others.proxy.perSource.inherit"), value: INHERIT },
    { label: t("configuration.others.proxy.doNotUse"), value: String(ProxyMode.DoNotUse) },
    { label: t("configuration.others.proxy.useSystem"), value: String(ProxyMode.UseSystem) },
    ...customProxies.map((c) => ({
      label: c.name ? `${c.name} (${c.address})` : (c.address ?? ""),
      value: `custom:${c.id}`,
    })),
  ];

  const selectedValue = (id: number): string => {
    const entry = overrides[String(id)];

    if (!entry) return INHERIT;
    if (entry.mode === ProxyMode.UseCustom) return `custom:${entry.customProxyId}`;

    return String(entry.mode);
  };

  const change = (id: number, value: string) => {
    setOverrides((prev) => {
      const next = { ...prev };

      // Inheriting carries no information, so drop the entry entirely rather than storing a
      // mode that means "same as global".
      if (value === INHERIT) {
        delete next[String(id)];

        return next;
      }

      next[String(id)] = value.startsWith("custom:")
        ? { mode: ProxyMode.UseCustom, customProxyId: value.slice("custom:".length) }
        : { mode: Number(value) as ProxyModel["mode"], customProxyId: undefined };

      return next;
    });
  };

  return (
    <Modal
      defaultVisible
      size="2xl"
      title={t<string>("configuration.others.proxy.perSource.title")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await BApi.options.patchNetworkOptions({ thirdPartyProxies: overrides });
      }}
    >
      <div className="flex flex-col gap-3">
        <p className="text-xs text-foreground-500">
          {t<string>("configuration.others.proxy.perSource.description")}
        </p>
        {ProxyCapableThirdPartyIds.map((id) => (
          <div key={id} className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2 text-sm">
              <ThirdPartyIcon thirdPartyId={id} />
              {ThirdPartyId[id]}
            </div>
            <div className="w-[280px]">
              <Select
                dataSource={options}
                multiple={false}
                selectedKeys={[selectedValue(id)]}
                size="sm"
                onSelectionChange={(keys) => {
                  const key = Array.from(keys)[0] as string;

                  if (key) change(id, key);
                }}
              />
            </div>
          </div>
        ))}
      </div>
    </Modal>
  );
};

ThirdPartyProxyModal.displayName = "ThirdPartyProxyModal";

export default ThirdPartyProxyModal;
