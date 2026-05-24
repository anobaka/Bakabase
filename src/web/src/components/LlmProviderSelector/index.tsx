"use client";

import type { BakabaseModulesAIModelsDbAiProviderDbModel } from "@/sdk/Api";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Select, SelectItem } from "@heroui/react";

import BApi from "@/sdk/BApi";
import { AiProviderKindLabel } from "@/sdk/constants";

type Provider = BakabaseModulesAIModelsDbAiProviderDbModel;

interface LlmProviderSelectorProps {
  value?: number;
  onChange?: (providerId: number | undefined) => void;
  placeholder?: string;
  size?: "sm" | "md" | "lg";
  className?: string;
}

/**
 * Hook returning AI providers that have the LLM capability enabled. Use this from
 * any UI that wants to pick an LLM-capable provider.
 */
export function useLlmProviders() {
  const [providers, setProviders] = useState<Provider[]>([]);

  const load = useCallback(async () => {
    const r = await BApi.ai.getAllAiProviders();

    if (!r.code && r.data) {
      setProviders(r.data.filter((p) => p.llmEnabled));
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
      const kindLabel = AiProviderKindLabel[p.kind] ?? "";

      return `${p.name} (${kindLabel})`;
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
      isClearable
      className={className}
      placeholder={placeholder}
      selectedKeys={value !== undefined ? new Set([String(value)]) : new Set<string>()}
      size={size}
      onSelectionChange={(keys) => {
        const key = Array.from(keys)[0];

        onChange?.(key !== undefined ? Number(key) : undefined);
      }}
    >
      {providers.map((p) => (
        <SelectItem key={String(p.id)}>
          {`${p.name} (${AiProviderKindLabel[p.kind] ?? ""})`}
        </SelectItem>
      ))}
    </Select>
  );
};

export default LlmProviderSelector;
