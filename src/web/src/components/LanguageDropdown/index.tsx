"use client";

import React from "react";

import { Select } from "@/components/bakaui";
import type { SelectProps } from "@/components/bakaui/components/Select";
import { useAppOptionsStore } from "@/stores/options";

const DEFAULT_LANGUAGES = [
  { code: "zh-CN", label: "简体中文" },
  { code: "zh-TW", label: "繁體中文" },
  { code: "en-US", label: "English" },
  { code: "ja-JP", label: "日本語" },
  { code: "ko-KR", label: "한국어" },
  { code: "fr-FR", label: "Français" },
  { code: "de-DE", label: "Deutsch" },
  { code: "es-ES", label: "Español" },
  { code: "pt-BR", label: "Português" },
  { code: "ru-RU", label: "Русский" },
  { code: "ar-SA", label: "العربية" },
  { code: "hi-IN", label: "हिन्दी" },
  { code: "th-TH", label: "ไทย" },
  { code: "vi-VN", label: "Tiếng Việt" },
  { code: "it-IT", label: "Italiano" },
];

type Props = {
  value?: string;
  defaultValue?: string;
  onValueChange?: (value: string) => void;
  availableLocales?: { code: string; label: string; shortLabel?: string }[];
} & Omit<SelectProps, "dataSource" | "selectedKeys" | "defaultSelectedKeys" | "onSelectionChange" | "children">;

const LanguageDropdown = ({
  value,
  defaultValue,
  onValueChange,
  availableLocales,
  ...selectProps
}: Props) => {
  const appLanguage = useAppOptionsStore((state) => state.data?.language);
  const languages = availableLocales ?? DEFAULT_LANGUAGES;
  const effectiveDefaultValue = defaultValue ?? appLanguage;
  const hasShortLabels = languages.some((l) => l.shortLabel);

  return (
    <Select
      aria-label="Language"
      {...selectProps}
      dataSource={languages.map((lang) => ({
        value: lang.code,
        label: lang.label,
      }))}
      selectedKeys={value ? [value] : undefined}
      defaultSelectedKeys={effectiveDefaultValue ? [effectiveDefaultValue] : undefined}
      onSelectionChange={(keys) => {
        const selected = Array.from(keys)[0] as string;
        if (selected) {
          onValueChange?.(selected);
        }
      }}
      {...(hasShortLabels
        ? {
            renderValue: (items) => {
              const selectedCode = items[0]?.key as string;
              const lang = languages.find((l) => l.code === selectedCode);
              return <span>{lang?.shortLabel ?? lang?.label ?? ""}</span>;
            },
            popoverProps: { placement: "top", className: "min-w-[140px]" },
          }
        : {})}
    />
  );
};

LanguageDropdown.displayName = "LanguageDropdown";

export default LanguageDropdown;
