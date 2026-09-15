"use client";

import type { Wrapper } from "@/core/models/Text/Wrapper.ts";
import type { DestroyableProps } from "@/components/bakaui/types.ts";
import type { IProperty } from "@/components/Property/models.ts";

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlinePlus, AiOutlineSearch } from "react-icons/ai";

import { useProfileModalSave } from "../useProfileModalSave";

import { Button, Chip, Input, Modal, Spinner, Textarea } from "@/components/bakaui";
import BApi from "@/sdk/BApi.tsx";
import {
  builtinPropertyForDisplayNames,
  PropertyPool,
  PropertyType,
  WellKnownTextType,
} from "@/sdk/constants.ts";
import { getEnumKey } from "@/i18n";

type Props = DestroyableProps & {
  template?: string;
  properties?: Omit<IProperty, "bizValueType" | "dbValueType">[];
  onSubmit?: (template: string) => unknown | Promise<unknown>;
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
  wrappers: Wrapper[],
): Array<{ type: "text" | "property" | "invalid" | "wrapper"; content: string }> => {
  const segments: Array<{ type: "text" | "property" | "invalid" | "wrapper"; content: string }> =
    [];
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
  const editor = useProfileModalSave(onSubmit, t<string>("resourceProfile.editor.saveFailed"));
  const [wrappers, setWrappers] = useState<Wrapper[]>([]);
  const [wrappersLoading, setWrappersLoading] = useState(true);
  const [wrapperError, setWrapperError] = useState<{ message?: string }>();
  const wrapperRequest = useRef(0);
  const [templateValue, setTemplateValue] = useState(template ?? "");
  const [keyword, setKeyword] = useState("");
  const [selectedType, setSelectedType] = useState<"all" | "builtin" | PropertyType>("all");
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const focusTimer = useRef<ReturnType<typeof setTimeout>>();

  // Property insertion stays available even if the optional wrapper catalogue cannot be loaded.
  const propertyItems = useMemo(() => {
    const items: PropertyItem[] = builtinPropertyForDisplayNames.map((value) => ({
      name: t<string>(getEnumKey("BuiltinPropertyForDisplayName", value.label)),
      type: "builtin",
    }));

    properties
      ?.filter((property) => property.pool === PropertyPool.Custom && property.name)
      .forEach((property) => items.push({ name: property.name!, type: property.type }));

    return Array.from(new Map(items.map((item) => [item.name, item])).values());
  }, [properties, t]);

  const loadWrappers = useCallback(async () => {
    const request = ++wrapperRequest.current;

    setWrappersLoading(true);
    setWrapperError(undefined);
    try {
      const types = await BApi.text.getAllTextTypes();

      if (types.code !== 0) throw new Error(types.message);
      const wrapperType = types.data?.find((type) => type.wellKnown === WellKnownTextType.Wrapper);
      let items: Wrapper[] = [];

      if (wrapperType) {
        const entries = await BApi.text.getTextEntries(wrapperType.id);

        if (entries.code !== 0) throw new Error(entries.message);
        items = (entries.data ?? [])
          .filter((entry) => entry.value1 && entry.value2)
          .map((entry) => ({ left: entry.value1!, right: entry.value2! }));
      }
      if (request === wrapperRequest.current) setWrappers(items);
    } catch (error) {
      if (request === wrapperRequest.current)
        setWrapperError({ message: error instanceof Error ? error.message : undefined });
    } finally {
      if (request === wrapperRequest.current) setWrappersLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadWrappers();

    return () => {
      wrapperRequest.current++;
      if (focusTimer.current) clearTimeout(focusTimer.current);
    };
  }, [loadWrappers]);

  const insertAtCursor = (text: string, cursorOffset = text.length) => {
    const start = textareaRef.current?.selectionStart ?? templateValue.length;
    const end = textareaRef.current?.selectionEnd ?? templateValue.length;

    setTemplateValue((value) => value.slice(0, start) + text + value.slice(end));
    if (focusTimer.current) clearTimeout(focusTimer.current);
    focusTimer.current = setTimeout(() => {
      textareaRef.current?.focus();
      textareaRef.current?.setSelectionRange(start + cursorOffset, start + cursorOffset);
    }, 0);
  };
  const insertWrapper = (wrapper: Wrapper) => {
    const start = textareaRef.current?.selectionStart ?? templateValue.length;
    const end = textareaRef.current?.selectionEnd ?? templateValue.length;
    const selected = templateValue.slice(start, end);

    insertAtCursor(
      `${wrapper.left}${selected}${wrapper.right}`,
      wrapper.left.length + selected.length,
    );
  };
  const availableTypes = new Set(propertyItems.map((item) => item.type));
  const typeFilters: { key: "all" | "builtin" | PropertyType; label: string }[] = [
    { key: "all", label: t<string>("common.label.all") },
    { key: "builtin", label: t<string>("common.label.builtin") },
    ...Object.entries(propertyTypeLabels)
      .filter(([key]) => availableTypes.has(Number(key) as PropertyType))
      .map(([key, label]) => ({
        key: Number(key) as PropertyType,
        label: t<string>(getEnumKey("PropertyType", label)),
      })),
  ];
  const filteredProperties = propertyItems.filter(
    (item) =>
      (selectedType === "all" || item.type === selectedType) &&
      item.name.toLocaleLowerCase().includes(keyword.trim().toLocaleLowerCase()),
  );
  const previewSegments = parseTemplateForPreview(
    templateValue,
    new Set(propertyItems.map((item) => item.name)),
    wrappers,
  );
  const unknownCount = previewSegments.filter((segment) => segment.type === "invalid").length;

  return (
    <Modal
      classNames={{ base: "max-w-3xl", body: "gap-5", footer: "border-t border-default-200/60" }}
      footer={
        <div className="flex w-full items-center justify-end gap-2">
          <Button isDisabled={editor.saving} variant="light" onPress={editor.close}>
            {t<string>("common.action.cancel")}
          </Button>
          <Button
            color="primary"
            isLoading={editor.saving}
            onPress={() => editor.save(templateValue)}
          >
            {t<string>("common.action.save")}
          </Button>
        </div>
      }
      hideCloseButton={editor.saving}
      isDismissable={!editor.saving}
      isKeyboardDismissDisabled={editor.saving}
      size="3xl"
      title={t<string>("resourceProfile.modal.editDisplayNameTemplateTitle")}
      visible={editor.visible}
      onClose={editor.close}
      onDestroyed={props.onDestroyed}
    >
      <p className="text-sm leading-6 text-default-600">
        {t<string>("resourceProfile.nameEditor.description")}
      </p>
      <Textarea
        ref={textareaRef}
        classNames={{ input: "font-mono text-sm" }}
        description={t<string>("resourceProfile.nameEditor.emptyFallback")}
        isDisabled={editor.saving}
        label={t<string>("resourceProfile.label.template")}
        maxRows={6}
        minRows={3}
        placeholder={t<string>("resourceProfile.input.templatePlaceholder")}
        value={templateValue}
        onValueChange={setTemplateValue}
      />
      {templateValue && (
        <section className="space-y-2 rounded-xl bg-default-50 p-3">
          <h3 className="text-xs font-medium text-default-600">
            {t<string>("resourceProfile.nameEditor.previewTitle")}
          </h3>
          <div
            aria-label={t<string>("resourceProfile.nameEditor.previewTitle")}
            className="flex flex-wrap items-center gap-0.5 whitespace-pre-wrap break-all text-sm leading-6"
          >
            {previewSegments.map((segment, index) =>
              segment.type === "text" ? (
                <span key={index}>{segment.content}</span>
              ) : (
                <Chip
                  key={index}
                  className="h-5"
                  color={
                    segment.type === "property"
                      ? "primary"
                      : segment.type === "wrapper"
                        ? "secondary"
                        : "danger"
                  }
                  size="sm"
                  variant="flat"
                >
                  {segment.content}
                </Chip>
              ),
            )}
          </div>
          <p className="text-xs leading-5 text-default-500">
            {t<string>("resourceProfile.nameEditor.previewHint")}
          </p>
          {unknownCount > 0 && (
            <p className="text-xs leading-5 text-warning-700">
              {t<string>("resourceProfile.nameEditor.unknownProperties", { count: unknownCount })}
            </p>
          )}
        </section>
      )}
      <section className="space-y-3 border-t border-default-200/60 pt-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h3 className="text-sm font-medium">
            {t<string>("resourceProfile.label.availableProperties")}
          </h3>
          <Input
            isClearable
            aria-label={t<string>("resourceProfile.nameEditor.searchProperties")}
            className="w-full sm:w-60"
            isDisabled={editor.saving}
            placeholder={t<string>("resourceProfile.nameEditor.searchProperties")}
            size="sm"
            startContent={<AiOutlineSearch aria-hidden />}
            value={keyword}
            onValueChange={setKeyword}
          />
        </div>
        <div className="flex flex-wrap gap-1.5">
          {typeFilters.map((filter) => (
            <Button
              key={String(filter.key)}
              aria-pressed={selectedType === filter.key}
              className="h-7 min-w-0 px-2 text-xs"
              color={selectedType === filter.key ? "primary" : "default"}
              isDisabled={editor.saving}
              size="sm"
              variant={selectedType === filter.key ? "flat" : "light"}
              onPress={() => setSelectedType(filter.key)}
            >
              {filter.label}
            </Button>
          ))}
        </div>
        <div className="max-h-40 overflow-y-auto">
          <div className="flex flex-wrap gap-2">
            {filteredProperties.map((property) => (
              <Button
                key={property.name}
                aria-label={t<string>("resourceProfile.nameEditor.insertProperty", {
                  name: property.name,
                })}
                className="h-auto min-h-8 max-w-full min-w-0 whitespace-normal py-1 text-left text-xs"
                color="primary"
                isDisabled={editor.saving}
                size="sm"
                startContent={<AiOutlinePlus aria-hidden className="shrink-0" />}
                variant="flat"
                onPress={() => insertAtCursor(`{${property.name}}`)}
              >
                {property.name}
              </Button>
            ))}
            {filteredProperties.length === 0 && (
              <p className="py-3 text-xs text-default-500">
                {t<string>("resourceProfile.nameEditor.noMatchingProperties")}
              </p>
            )}
          </div>
        </div>
      </section>
      <section className="space-y-2">
        <h3 className="text-sm font-medium">{t<string>("resourceProfile.label.wrappers")}</h3>
        {wrappersLoading ? (
          <Spinner label={t<string>("common.state.loading")} size="sm" />
        ) : wrapperError ? (
          <div
            className="flex items-center justify-between gap-3 rounded-lg bg-warning/10 px-3 py-2 text-xs text-warning-700"
            role="alert"
          >
            <span>
              {wrapperError.message || t<string>("resourceProfile.nameEditor.wrappersLoadFailed")}
            </span>
            <Button isDisabled={editor.saving} size="sm" variant="light" onPress={loadWrappers}>
              {t<string>("common.action.retry")}
            </Button>
          </div>
        ) : wrappers.length ? (
          <div className="flex max-h-28 flex-wrap gap-2 overflow-y-auto">
            {wrappers.map((wrapper, index) => (
              <Button
                key={`${wrapper.left}:${wrapper.right}:${index}`}
                aria-label={t<string>("resourceProfile.nameEditor.insertWrapper", {
                  left: wrapper.left,
                  right: wrapper.right,
                })}
                className="h-8 min-w-10 font-mono"
                color="secondary"
                isDisabled={editor.saving}
                size="sm"
                variant="flat"
                onPress={() => insertWrapper(wrapper)}
              >
                {wrapper.left} {wrapper.right}
              </Button>
            ))}
          </div>
        ) : (
          <p className="text-xs text-default-500">
            {t<string>("resourceProfile.nameEditor.noWrappers")}
          </p>
        )}
        <p className="text-xs leading-5 text-default-500">
          {t<string>("resourceProfile.tip.wrappersRemovedAutomatically")}
        </p>
      </section>
      {editor.error && (
        <p className="rounded-lg bg-danger/10 px-3 py-2 text-sm text-danger" role="alert">
          {editor.error}
        </p>
      )}
    </Modal>
  );
};

DisplayNameTemplateEditorModal.displayName = "DisplayNameTemplateEditorModal";
export default DisplayNameTemplateEditorModal;
