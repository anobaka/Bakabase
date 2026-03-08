"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Select, SelectItem } from "@heroui/react";

import BApi from "@/sdk/BApi";
import type { BakabaseModulesAIModelsDbLlmProviderConfigDbModel } from "@/sdk/Api";
import { LlmProviderTypeLabel } from "@/sdk/constants";

type Provider = BakabaseModulesAIModelsDbLlmProviderConfigDbModel;

interface LlmProviderSelectorProps {
  value?: number;
  onChange?: (providerId: number | undefined) => void;
  placeholder?: string;
  size?: "sm" | "md" | "lg";
  className?: string;
}

export function useLlmProviders() {
  const [providers, setProviders] = useState<Provider[]>([]);

  const load = useCallback(async () => {
    const r = await BApi.ai.getAllLlmProviders();
    if (!r.code && r.data) {
      setProviders(r.data);
    }
  }, []);

  useEffect(() => {
    load();
  }, []);

  const providerMap = useMemo(() => {
    const m: Record<number, Provider> = {};
    for (const p of providers) {
      m[p.id] = p;
    }
    return m;
  }, [providers]);

  const getProviderLabel = useCallback(
    (providerId: number) => {
      const p = providerMap[providerId];
      if (!p) return `#${providerId}`;
      const typeLabel = LlmProviderTypeLabel[p.providerType] ?? "";
      return `${p.name} (${typeLabel})`;
    },
    [providerMap],
  );

  return { providers, providerMap, getProviderLabel, reload: load };
}

const LlmProviderSelector = ({
  value,
  onChange,
  placeholder,
  size = "sm",
  className,
}: LlmProviderSelectorProps) => {
  const { providers } = useLlmProviders();

  return (
    <Select
      size={size}
      className={className}
      placeholder={placeholder}
      selectedKeys={value !== undefined ? new Set([String(value)]) : new Set<string>()}
      onSelectionChange={(keys) => {
        const key = Array.from(keys)[0];
        onChange?.(key !== undefined ? Number(key) : undefined);
      }}
      isClearable
    >
      {providers.map((p) => (
        <SelectItem key={String(p.id)}>
          {`${p.name} (${LlmProviderTypeLabel[p.providerType] ?? ""})`}
        </SelectItem>
      ))}
    </Select>
  );
};

export default LlmProviderSelector;
