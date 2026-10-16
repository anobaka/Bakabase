"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React, { useMemo } from "react";
import { useTranslation } from "react-i18next";

import { normalizeLegacyPropertyMarkConfig } from "./MarkConfigModal/utils";

import {
  PathMarkType,
  PropertyValueType,
  PathMatchMode,
  PathMarkApplyScope,
  PathMarkValueBasePath,
} from "@/sdk/constants";

type Props = {
  mark: BakabaseAbstractionsModelsDomainPathMark;
  className?: string;
  /** Mark type label (e.g., "Resource", "Property", "Media Library") */
  label?: string;
  /** Priority number to display as subscript */
  priority?: number;
};

const MarkDescription = ({ mark, className, label, priority }: Props) => {
  const { t } = useTranslation();

  const config = useMemo(() => {
    try {
      return normalizeLegacyPropertyMarkConfig(
        JSON.parse(mark.configJson || "{}"),
        mark.type as PathMarkType,
      );
    } catch {
      return {};
    }
  }, [mark.configJson, mark.type]);

  const getDescription = (): string => {
    try {
      const matchMode = config.matchMode;
      const parts: string[] = [];

      // Build type label with additional info
      let typeLabel = label || "";

      // For property marks, append property name
      if (mark.type === PathMarkType.Property && mark.property?.name) {
        typeLabel = `${typeLabel}:${mark.property.name}`;
      }

      // For media library marks with fixed value, append library name
      if (mark.type === PathMarkType.MediaLibrary) {
        const valueType = config.valueType ?? PropertyValueType.Fixed;

        if (valueType === PropertyValueType.Fixed && mark.mediaLibrary?.name) {
          typeLabel = `${typeLabel}:${mark.mediaLibrary.name}`;
        }
      }

      if (typeLabel) {
        // Add priority as subscript if > 0
        const prioritySuffix = priority && priority > 0 ? `₍${priority}₎` : "";

        parts.push(`[${typeLabel}]${prioritySuffix}`);
      }

      // Match mode description - simplified layer format
      const applyScope = config.applyScope ?? PathMarkApplyScope.MatchedOnly;
      const includesSubdirs = applyScope === PathMarkApplyScope.MatchedAndSubdirectories;

      if (matchMode === PathMatchMode.Layer) {
        const layer = config.layer;

        if (layer === undefined || layer === null) {
          parts.push(t("markDescription.invalid"));
        } else if (layer === 0) {
          if (includesSubdirs) {
            parts.push(t("markDescription.layer.currentAndSubdirs"));
          } else {
            parts.push(t("markDescription.layer.current"));
          }
        } else if (layer > 0) {
          const layerText = `+${layer}${t("markDescription.layer.suffix")}`;

          parts.push(
            includesSubdirs ? `${layerText}${t("markDescription.andSubdirs")}` : layerText,
          );
        } else {
          const layerText = `${layer}${t("markDescription.layer.suffix")}`;

          parts.push(
            includesSubdirs ? `${layerText}${t("markDescription.andSubdirs")}` : layerText,
          );
        }
      } else if (matchMode === PathMatchMode.Regex) {
        const regex = config.regex;

        if (regex) {
          const regexText = t("markDescription.regex", { regex });

          parts.push(
            includesSubdirs ? `${regexText}${t("markDescription.andSubdirs")}` : regexText,
          );
        } else {
          parts.push(t("markDescription.invalid"));
        }
      } else {
        parts.push(t("markDescription.invalid"));
      }

      if (mark.type === PathMarkType.Property || mark.type === PathMarkType.MediaLibrary) {
        const isLibrary = mark.type === PathMarkType.MediaLibrary;
        const valueType = config.valueType ?? PropertyValueType.Fixed;

        if (valueType === PropertyValueType.Fixed && !isLibrary && config.fixedValue != null) {
          parts.push(`="${config.fixedValue}"`);
        } else if (valueType === PropertyValueType.Dynamic) {
          const prefix = isLibrary ? "→" : "=";
          const legacyRegex = !isLibrary && config.valueRegexMatchesResourcePath;
          const mode =
            config.valueMatchMode ??
            (isLibrary ? matchMode : config.valueRegex ? PathMatchMode.Regex : PathMatchMode.Layer);
          const resourceBase = config.valueBasePath === PathMarkValueBasePath.ResourcePath;
          const base = t(
            resourceBase ? "markDescription.base.resource" : "markDescription.base.mark",
          );
          const valueRegex = isLibrary ? config.regexToMediaLibrary : config.valueRegex;
          const valueLayer = isLibrary ? (config.layerToMediaLibrary ?? 0) : config.valueLayer;

          if (legacyRegex) {
            parts.push(`${prefix}${t("markDescription.value.legacyRegex", { regex: valueRegex })}`);
          } else if (mode === PathMatchMode.Regex && valueRegex) {
            parts.push(`${prefix}${t("markDescription.value.regex", { base, regex: valueRegex })}`);
          } else if (mode === PathMatchMode.Layer && Number.isInteger(valueLayer)) {
            if (resourceBase && valueLayer > 0) parts.push(t("markDescription.invalid"));
            else
              parts.push(
                `${prefix}${t(valueLayer === 0 ? "markDescription.value.self" : valueLayer > 0 ? "markDescription.value.descendant" : "markDescription.value.ancestor", { base, layer: Math.abs(valueLayer) })}`,
              );
          } else parts.push(t("markDescription.invalid"));
        }
      }

      // For resource marks
      if (mark.type === PathMarkType.Resource) {
        const fsTypeFilter = config.fsTypeFilter;

        if (fsTypeFilter === 1) {
          parts.push(t("markDescription.fileOnly"));
        } else if (fsTypeFilter === 2) {
          parts.push(t("markDescription.dirOnly"));
        }

        const extensions = config.extensions;

        if (extensions && extensions.length > 0) {
          parts.push(extensions.join(","));
        }
      }

      return parts.length > 0 ? parts.join(" | ") : t("markDescription.empty");
    } catch (error) {
      console.error("Failed to parse mark config:", error);

      return t("markDescription.invalid");
    }
  };

  return <span className={className}>{getDescription()}</span>;
};

MarkDescription.displayName = "MarkDescription";

export default MarkDescription;
