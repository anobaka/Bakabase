"use client";

import type { Wrapper } from "@/core/models/Text/Wrapper.ts";
import type { DestroyableProps } from "@/components/bakaui/types.ts";
import type { IProperty } from "@/components/Property/models.ts";

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { InfoCircleOutlined } from "@ant-design/icons";

import { Chip, Modal, Textarea, Divider } from "@/components/bakaui";
import BApi from "@/sdk/BApi.tsx";
import { builtinPropertyForDisplayNames, PropertyType, SpecialTextType } from "@/sdk/constants.ts";

type Props = DestroyableProps & {
  template?: string;
  properties?: Omit<IProperty, "bizValueType" | "dbValueType">[];
  onSubmit?: (template: string) => any;
};

// Property with type info for filtering
type PropertyItem = {
  name: string;
  type: "builtin" | PropertyType;
};

// Parse template into segments for preview
const parseTemplateForPreview = (
  template: string,
  validPropertyNames: Set<string>,
  wrappers: Wrapper[]
): Array<{ type: "text" | "property" | "invalid" | "wrapper"; content: string }> => {
  const segments: Array<{ type: "text" | "property" | "invalid" | "wrapper"; content: string }> = [];
  const regex = /\{([^}]+)\}/g;
  let lastIndex = 0;
  let match;

  // Build wrapper chars set
  const wrapperChars = new Set<string>();
  wrappers.forEach((w) => {
    wrapperChars.add(w.left);
    wrapperChars.add(w.right);
  });

  const processText = (text: string) => {
    // Check for wrapper characters in text
    let i = 0;
    while (i < text.length) {
      let foundWrapper = false;
      for (const w of wrapperChars) {
        if (text.substring(i, i + w.length) === w) {
          if (i > 0) {
            const before = text.substring(0, i);
            if (before) segments.push({ type: "text", content: before });
          }
          segments.push({ type: "wrapper", content: w });
          text = text.substring(i + w.length);
          i = 0;
          foundWrapper = true;
          break;
        }
      }
      if (!foundWrapper) i++;
    }
    if (text) segments.push({ type: "text", content: text });
  };

  while ((match = regex.exec(template)) !== null) {
    if (match.index > lastIndex) {
      processText(template.slice(lastIndex, match.index));
    }
    const propName = match[1];
    const isValid = validPropertyNames.has(propName);
    segments.push({ type: isValid ? "property" : "invalid", content: propName });
    lastIndex = regex.lastIndex;
  }

  if (lastIndex < template.length) {
    processText(template.slice(lastIndex));
  }

  return segments;
};

// PropertyType labels for display
const propertyTypeLabels: Record<PropertyType, string> = {
  [PropertyType.SingleLineText]: "SingleLineText",
  [PropertyType.MultilineText]: "MultilineText",
  [PropertyType.SingleChoice]: "SingleChoice",
  [PropertyType.MultipleChoice]: "MultipleChoice",
  [PropertyType.Number]: "Number",
  [PropertyType.Percentage]: "Percentage",
  [PropertyType.Rating]: "Rating",
  [PropertyType.Boolean]: "Boolean",
  [PropertyType.Link]: "Link",
  [PropertyType.Attachment]: "Attachment",
  [PropertyType.Date]: "Date",
  [PropertyType.DateTime]: "DateTime",
  [PropertyType.Time]: "Time",
  [PropertyType.Formula]: "Formula",
  [PropertyType.Multilevel]: "Multilevel",
  [PropertyType.Tags]: "Tags",
};

const DisplayNameTemplateEditorModal = ({ template, properties, onSubmit, ...props }: Props) => {
  const { t } = useTranslation();
  const [wrappers, setWrappers] = useState<Wrapper[]>([]);
  const [propertyItems, setPropertyItems] = useState<PropertyItem[]>([]);
  const [templateValue, setTemplateValue] = useState(template ?? "");
  const [selectedType, setSelectedType] = useState<"all" | "builtin" | PropertyType>("all");
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const init = useCallback(async () => {
    const tr = await BApi.specialText.getAllSpecialTexts();
    const texts = tr.data?.[SpecialTextType.Wrapper] || [];
    const wrappersData = texts.map((text) => ({
      left: text.value1!,
      right: text.value2!,
    }));
    setWrappers(wrappersData);

    // Build property items with type info
    const items: PropertyItem[] = [];

    // Add builtin properties
    builtinPropertyForDisplayNames.forEach((v) => {
      items.push({
        name: t(`BuiltinPropertyForDisplayName.${v.label}`),
        type: "builtin",
      });
    });

    // Add custom properties with their type
    properties?.forEach((cp) => {
      if (cp.name) {
        items.push({
          name: cp.name,
          type: cp.type,
        });
      }
    });

    setPropertyItems(items);
  }, [properties, t]);

  useEffect(() => {
    init();
  }, [init]);

  const insertAtCursor = (text: string) => {
    const textarea = textareaRef.current;
    if (!textarea) {
      setTemplateValue((prev) => prev + text);
      return;
    }

    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const newValue = templateValue.slice(0, start) + text + templateValue.slice(end);
    setTemplateValue(newValue);

    // Restore cursor position after the inserted text
    setTimeout(() => {
      textarea.focus();
      textarea.setSelectionRange(start + text.length, start + text.length);
    }, 0);
  };

  // Get available type filters based on existing properties
  const availableTypes = useMemo(() => {
    const types = new Set<"builtin" | PropertyType>();
    propertyItems.forEach((item) => types.add(item.type));
    return types;
  }, [propertyItems]);

  // Filter properties by selected type
  const filteredProperties = useMemo(() => {
    if (selectedType === "all") return propertyItems;
    return propertyItems.filter((item) => item.type === selectedType);
  }, [propertyItems, selectedType]);

  const validPropertyNames = new Set(propertyItems.map((p) => p.name));
  const previewSegments = parseTemplateForPreview(templateValue, validPropertyNames, wrappers);

  // Build type filters dynamically based on available types
  const typeFilters = useMemo(() => {
    const filters: Array<{ key: "all" | "builtin" | PropertyType; label: string }> = [
      { key: "all", label: t("All") },
    ];

    // Add builtin if exists
    if (availableTypes.has("builtin")) {
      filters.push({ key: "builtin", label: t("Builtin") });
    }

    // Add property types that exist
    Object.entries(propertyTypeLabels).forEach(([key, label]) => {
      const typeKey = Number(key) as PropertyType;
      if (availableTypes.has(typeKey)) {
        filters.push({ key: typeKey, label: t(`PropertyType.${label}`) });
      }
    });

    return filters;
  }, [availableTypes, t]);

  return (
    <Modal
      defaultVisible
      size="2xl"
      title={t("Edit display name template for resources")}
      onDestroyed={props.onDestroyed}
      onOk={() => onSubmit?.(templateValue)}
    >
      <div className="flex flex-col gap-4">
        {/* Properties Section */}
        <div>
          <div className="flex items-center justify-between mb-2">
            <div className="text-sm font-medium">{t("Available Properties")}</div>
          </div>
          {/* Type filters */}
          <div className="flex flex-wrap gap-1 mb-2">
            {typeFilters.map((filter) => (
              <Chip
                key={String(filter.key)}
                size="sm"
                color={selectedType === filter.key ? "primary" : "default"}
                variant={selectedType === filter.key ? "solid" : "flat"}
                className="cursor-pointer"
                onClick={() => setSelectedType(filter.key)}
              >
                {filter.label}
              </Chip>
            ))}
          </div>
          {/* Properties list with scroll */}
          <div className="max-h-[240px] overflow-y-auto">
            <div className="flex flex-wrap gap-1">
              {filteredProperties.map((p) => (
                <Chip
                  key={p.name}
                  size="sm"
                  color="primary"
                  variant="flat"
                  className="cursor-pointer hover:opacity-80"
                  onClick={() => insertAtCursor(`{${p.name}}`)}
                >
                  {p.name}
                </Chip>
              ))}
              {filteredProperties.length === 0 && (
                <span className="text-default-400 text-sm">{t("No properties available")}</span>
              )}
            </div>
          </div>
        </div>

        {/* Wrappers Section */}
        {wrappers.length > 0 && (
          <div>
            <div className="text-sm font-medium mb-2">{t("Wrappers")}</div>
            <div className="flex flex-wrap gap-1">
              {wrappers.map((w) => (
                <Chip
                  key={w.left}
                  size="sm"
                  color="secondary"
                  variant="flat"
                  className="cursor-pointer hover:opacity-80"
                  onClick={() => insertAtCursor(`${w.left}${w.right}`)}
                >
                  {w.left} {w.right}
                </Chip>
              ))}
            </div>
            <div className="text-xs text-default-400 mt-1">
              <InfoCircleOutlined className="mr-1" />
              {t("Wrappers around empty property values will be removed automatically.")}
            </div>
          </div>
        )}

        <Divider />

        {/* Input */}
        <div>
          <div className="text-sm font-medium mb-2">{t("Template")}</div>
          <Textarea
            ref={textareaRef}
            placeholder={t("Enter template or click properties above to insert...")}
            value={templateValue}
            onValueChange={setTemplateValue}
            minRows={2}
            maxRows={5}
            classNames={{
              input: "font-mono",
            }}
          />
          <div className="text-xs text-default-400 mt-1">
            <InfoCircleOutlined className="mr-1" />
            {t("Leave empty to use filename as display name.")}
          </div>
        </div>

        {/* Preview */}
        {templateValue && (
          <div>
            <div className="text-sm font-medium mb-2">{t("Preview")}</div>
            <div className="p-3 bg-default-100 rounded-lg flex flex-wrap items-center gap-0.5">
              {previewSegments.map((seg, idx) => {
                if (seg.type === "text") {
                  return <span key={idx}>{seg.content}</span>;
                }
                if (seg.type === "property") {
                  return (
                    <Chip key={idx} size="sm" color="primary" variant="flat" className="h-5">
                      {seg.content}
                    </Chip>
                  );
                }
                if (seg.type === "wrapper") {
                  return (
                    <Chip key={idx} size="sm" color="secondary" variant="flat" className="h-5">
                      {seg.content}
                    </Chip>
                  );
                }
                // invalid
                return (
                  <Chip key={idx} size="sm" color="danger" variant="flat" className="h-5">
                    {seg.content}
                  </Chip>
                );
              })}
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};

DisplayNameTemplateEditorModal.displayName = "DisplayNameTemplateEditorModal";

export default DisplayNameTemplateEditorModal;
