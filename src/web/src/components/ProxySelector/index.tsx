import type {
  BakabaseInsideWorldModelsConfigsNetworkOptionsProxyModel as ProxyModel,
  BakabaseInsideWorldModelsConfigsNetworkOptionsProxyOptions as ProxyOptions,
} from "@/sdk/Api";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Select } from "@/components/bakaui";

export type { ProxyModel, ProxyOptions };
export const ProxyMode = { DoNotUse: 0, UseSystem: 1, UseCustom: 2 } as const;
const INHERIT = "inherit";
const CUSTOM_PREFIX = "custom:";

type Props = {
  value?: ProxyModel;
  customProxies?: ProxyOptions[];
  onChange: (value: ProxyModel | undefined) => void;
  allowInherit?: boolean;
  label?: ReactNode;
  description?: ReactNode;
  className?: string;
  size?: "sm" | "md" | "lg";
  isCustomProxySupported?: (proxy: ProxyOptions) => boolean;
  unsupportedDescription?: string;
};

/** The same proxy choices for global settings, site overrides and individual download protocols. */
export default function ProxySelector({
  value,
  customProxies = [],
  onChange,
  allowInherit = false,
  label,
  description,
  className,
  size = "sm",
  isCustomProxySupported,
  unsupportedDescription,
}: Props) {
  const { t } = useTranslation();
  const selected =
    !value && allowInherit
      ? INHERIT
      : value?.mode === ProxyMode.UseCustom
        ? `${CUSTOM_PREFIX}${value.customProxyId}`
        : String(value?.mode ?? ProxyMode.DoNotUse);
  const options: { value: string; label: string; description?: string }[] = [
    ...(allowInherit
      ? [{ value: INHERIT, label: t<string>("thirdPartyConfig.field.proxy.inherit") }]
      : []),
    { value: String(ProxyMode.DoNotUse), label: t<string>("configuration.others.proxy.doNotUse") },
    {
      value: String(ProxyMode.UseSystem),
      label: t<string>("configuration.others.proxy.useSystem"),
    },
    ...customProxies.map((proxy) => ({
      value: `${CUSTOM_PREFIX}${proxy.id}`,
      label: proxy.name ? `${proxy.name} (${proxy.address})` : proxy.address,
      description:
        isCustomProxySupported && !isCustomProxySupported(proxy)
          ? unsupportedDescription
          : undefined,
    })),
  ];
  const disabledKeys = customProxies
    .filter((proxy) => isCustomProxySupported && !isCustomProxySupported(proxy))
    .map((proxy) => `${CUSTOM_PREFIX}${proxy.id}`);
  if (
    value?.mode === ProxyMode.UseCustom &&
    !customProxies.some((proxy) => proxy.id === value.customProxyId)
  ) {
    options.push({ value: selected, label: t<string>("configuration.others.proxy.unavailable") });
    disabledKeys.push(selected);
  }
  return (
    <Select
      disallowEmptySelection
      aria-label={typeof label === "string" ? label : t<string>("configuration.others.proxy")}
      label={label}
      description={description}
      className={className}
      dataSource={options}
      disabledKeys={disabledKeys}
      selectedKeys={[selected]}
      size={size}
      onSelectionChange={(keys) => {
        const key = Array.from(keys)[0];
        if (key == null || disabledKeys.includes(String(key))) return;
        const selection = String(key);
        if (selection === INHERIT && allowInherit) onChange(undefined);
        else if (selection.startsWith(CUSTOM_PREFIX))
          onChange({
            mode: ProxyMode.UseCustom,
            customProxyId: selection.slice(CUSTOM_PREFIX.length),
          });
        else if (
          selection === String(ProxyMode.DoNotUse) ||
          selection === String(ProxyMode.UseSystem)
        )
          onChange({ mode: Number(selection) as ProxyModel["mode"], customProxyId: undefined });
      }}
    />
  );
}
