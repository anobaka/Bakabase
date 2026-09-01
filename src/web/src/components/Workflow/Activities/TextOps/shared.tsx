"use client";

import type { components } from "@/sdk/BApi2";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Select } from "@/components/bakaui";
import { TextMatchMode, TextTypeShape, textMatchModes } from "@/sdk/constants";

export type TextTypeVm = components["schemas"]["Bakabase.Abstractions.Models.Domain.TextTypeDescriptor"];

/**
 * One fetch per page load, shared by every text node's form and summary. Types added on the
 * text management page while the editor is open show up after a reload — acceptable for a
 * vocabulary that changes rarely.
 */
let typesCache: Promise<TextTypeVm[]> | null = null;

export function fetchTextTypes(): Promise<TextTypeVm[]> {
  typesCache ??= BApi.text.getAllTextTypes().then((r) => (r.data ?? []) as TextTypeVm[]);

  return typesCache;
}

export function useTextTypes(): TextTypeVm[] | null {
  const [types, setTypes] = useState<TextTypeVm[] | null>(null);

  useEffect(() => {
    let cancelled = false;

    void fetchTextTypes().then((list) => {
      if (!cancelled) setTypes(list);
    });

    return () => {
      cancelled = true;
    };
  }, []);

  return types;
}

export const TextTypeSelect: React.FC<{
  types: TextTypeVm[];
  /** Restrict the choices to one shape — removeWrapped's wrappers must be DelimiterPair. */
  shape?: TextTypeShape;
  value: number;
  label: string;
  description?: string;
  onChange: (id: number) => void;
}> = ({ types, shape, value, label, description, onChange }) => {
  const options = types
    .filter((t) => shape === undefined || t.shape === shape)
    .map((t) => ({ value: String(t.id), label: `${t.name} · ${t.entryCount}` }));

  return (
    <Select
      dataSource={options}
      description={description}
      label={label}
      selectedKeys={value > 0 ? [String(value)] : []}
      onSelectionChange={(keys) => {
        const next = Array.from(keys)[0] as string | undefined;

        onChange(next ? parseInt(next, 10) : 0);
      }}
    />
  );
};

export const MatchModeSelect: React.FC<{
  value: TextMatchMode;
  onChange: (mode: TextMatchMode) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const options = textMatchModes.map((m) => ({
    value: String(m.value),
    label: t<string>(`workflow.activity.textOps.mode.${m.label}`),
  }));

  return (
    <Select
      dataSource={options}
      label={t<string>("workflow.activity.textOps.mode.label")}
      selectedKeys={[String(value)]}
      onSelectionChange={(keys) => {
        const next = Array.from(keys)[0] as string | undefined;

        if (next) onChange(parseInt(next, 10) as TextMatchMode);
      }}
    />
  );
};

/** Resolve a type's display name for summaries; falls back to "#id" until types load. */
export function typeName(types: TextTypeVm[] | null, id: number): string {
  return types?.find((t) => t.id === id)?.name ?? `#${id}`;
}
