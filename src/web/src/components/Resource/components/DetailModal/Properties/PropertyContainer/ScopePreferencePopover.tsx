"use client";

import type {
  Property,
  PropertyValueScopePreference,
  PropertyValueScopePriority,
} from "@/core/models/Resource";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  MdAdd,
  MdArrowDownward,
  MdArrowUpward,
  MdClose,
  MdTune,
} from "react-icons/md";

import BApi from "@/sdk/BApi";
import {
  PropertyPool,
  PropertyValueScope,
  PropertyValueScopeLabel,
  propertyValueScopes,
} from "@/sdk/constants";
import { Button, Popover, Switch } from "@/components/bakaui";

type Props = {
  resourceId: number;
  propertyPool: PropertyPool;
  propertyId: number;
  /** All scope-value entries for this property. */
  values?: Property["values"];
  /** Current preference for this (resource, property); undefined when no override is saved. */
  preference?: PropertyValueScopePreference;
  /** Priority array currently in effect after resolution (override or default). */
  effectivePriority: PropertyValueScope[];
  /** Called after a save/reset so the caller can refetch. */
  onChanged: () => void;
  /** Notifies parent when the popover opens/closes (for keeping the trigger button visible). */
  onOpenChange?: (open: boolean) => void;
};

const hasValue = (v?: { value?: any; bizValue?: any }) => {
  const x = v?.bizValue ?? v?.value;
  if (x == null) return false;
  if (Array.isArray(x)) return x.length > 0;
  if (typeof x === "string") return x.trim().length > 0;
  return true;
};

const previewText = (v?: { value?: any; bizValue?: any }) => {
  const x = v?.bizValue ?? v?.value;
  if (x == null) return "";
  if (Array.isArray(x)) return x.map((i) => (typeof i === "object" ? JSON.stringify(i) : String(i))).join(", ");
  if (typeof x === "object") return JSON.stringify(x);
  return String(x);
};

const ScopePreferencePopover = ({
  resourceId,
  propertyPool,
  propertyId,
  values,
  preference,
  effectivePriority,
  onChanged,
  onOpenChange,
}: Props) => {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [priorities, setPriorities] = useState<PropertyValueScopePriority[] | null>(null);
  const [dirty, setDirty] = useState(false);

  const initialize = () => {
    setPriorities(preference?.priorities ?? null);
    setDirty(false);
  };

  const nonEmptyScopes = useMemo(
    () =>
      propertyValueScopes
        .map((s) => ({
          scope: s.value,
          label: PropertyValueScopeLabel[s.value],
          value: values?.find((v) => v.scope === s.value),
        }))
        .filter((s) => hasValue(s.value)),
    [values],
  );

  const effectiveScope = useMemo(() => {
    for (const s of effectivePriority) {
      const v = values?.find((x) => x.scope === s);
      if (hasValue(v)) return s;
    }
    return undefined;
  }, [effectivePriority, values]);

  const indexOfScope = (scope: PropertyValueScope) =>
    priorities?.findIndex((p) => p.scope === scope) ?? -1;

  const addScope = (scope: PropertyValueScope) => {
    setPriorities([...(priorities ?? []), { scope, fallbackOnEmpty: true }]);
    setDirty(true);
  };

  const removeScope = (scope: PropertyValueScope) => {
    if (!priorities) return;
    const next = priorities.filter((p) => p.scope !== scope);

    setPriorities(next.length === 0 ? null : next);
    setDirty(true);
  };

  const moveScope = (scope: PropertyValueScope, dir: -1 | 1) => {
    if (!priorities) return;
    const idx = priorities.findIndex((p) => p.scope === scope);

    if (idx < 0) return;
    const target = idx + dir;

    if (target < 0 || target >= priorities.length) return;
    const next = [...priorities];

    [next[idx], next[target]] = [next[target], next[idx]];
    setPriorities(next);
    setDirty(true);
  };

  const setFallback = (scope: PropertyValueScope, fallbackOnEmpty: boolean) => {
    if (!priorities) return;
    const idx = priorities.findIndex((p) => p.scope === scope);

    if (idx < 0) return;
    const next = [...priorities];

    next[idx] = { ...next[idx], fallbackOnEmpty };
    setPriorities(next);
    setDirty(true);
  };

  const handleSave = async () => {
    await BApi.resource.putResourcePropertyValueScopePreference(resourceId, {
      propertyPool,
      propertyId,
      priorities: priorities ?? undefined,
    });
    setOpen(false);
    onChanged();
  };

  const handleReset = async () => {
    await BApi.resource.deleteResourcePropertyValueScopePreference(resourceId, {
      propertyPool,
      propertyId,
    });
    setOpen(false);
    onChanged();
  };

  const priorityLen = priorities?.length ?? 0;

  return (
    <Popover
      visible={open}
      onVisibleChange={(v) => {
        if (v) initialize();
        setOpen(v);
        onOpenChange?.(v);
      }}
      trigger={
        <button
          aria-label={t("property.scopePreference.title")}
          className="inline-flex items-center justify-center leading-none p-0 m-0 cursor-pointer opacity-60 hover:opacity-100 outline-none focus-visible:opacity-100"
          title={t("property.scopePreference.title")}
          type="button"
        >
          <MdTune size={12} />
        </button>
      }
    >
      <div className="flex flex-col gap-2 p-1 min-w-[340px] max-w-[460px]">
        <div className="text-xs opacity-60">
          {effectiveScope !== undefined
            ? `${t("property.scopePreference.currentlyShowing", { scope: PropertyValueScopeLabel[effectiveScope] })} · ${preference ? t("property.scopePreference.viaOverride") : t("property.scopePreference.viaDefault")}`
            : t("property.scopePreference.currentlyBlank")}
        </div>

        <div className="flex flex-col gap-1">
          {nonEmptyScopes.map((s) => {
            const idx = indexOfScope(s.scope);
            const inList = idx >= 0;
            const isLast = inList && idx === priorityLen - 1;
            const entry = inList ? priorities![idx] : undefined;
            // A non-last entry with fallbackOnEmpty=false cuts off the chain; anything after it is inert.
            const cutoffIdx = priorities?.findIndex(
              (p, i) => !p.fallbackOnEmpty && i < priorityLen - 1,
            ) ?? -1;
            const isCutOff = inList && cutoffIdx >= 0 && idx > cutoffIdx;

            return (
              <div
                className={`flex items-center gap-1 text-sm ${isCutOff ? "opacity-40" : ""}`}
                key={s.scope}
              >
                {inList ? (
                  <>
                    <span className="font-mono text-xs opacity-60 w-5 text-right">
                      {idx + 1}.
                    </span>
                    <Button
                      isIconOnly
                      isDisabled={isCutOff || idx === 0}
                      size="sm"
                      title={t("property.scopePreference.moveUp")}
                      variant="light"
                      onPress={() => moveScope(s.scope, -1)}
                    >
                      <MdArrowUpward size={12} />
                    </Button>
                    <Button
                      isIconOnly
                      isDisabled={isCutOff || isLast}
                      size="sm"
                      title={t("property.scopePreference.moveDown")}
                      variant="light"
                      onPress={() => moveScope(s.scope, 1)}
                    >
                      <MdArrowDownward size={12} />
                    </Button>
                    <Button
                      isIconOnly
                      isDisabled={isCutOff}
                      size="sm"
                      title={t("property.scopePreference.removeFromList")}
                      variant="light"
                      onPress={() => removeScope(s.scope)}
                    >
                      <MdClose size={12} />
                    </Button>
                  </>
                ) : (
                  <Button
                    isIconOnly
                    size="sm"
                    title={t("property.scopePreference.addToList")}
                    variant="light"
                    onPress={() => addScope(s.scope)}
                  >
                    <MdAdd size={14} />
                  </Button>
                )}
                <span className={`flex-1 truncate ${inList ? "" : "opacity-60"}`}>
                  <span className="font-medium">{s.label}</span>
                  <span className="opacity-60 ml-2">{previewText(s.value)}</span>
                </span>
                {inList && (
                  <Switch
                    isDisabled={isCutOff || isLast}
                    isSelected={isLast ? true : entry!.fallbackOnEmpty}
                    size="sm"
                    title={
                      isCutOff
                        ? t("property.scopePreference.cutOff")
                        : isLast
                          ? t("property.scopePreference.fallbackDisabledOnLast")
                          : t("property.scopePreference.fallbackOnEmpty")
                    }
                    onValueChange={(v) => setFallback(s.scope, v)}
                  />
                )}
              </div>
            );
          })}
          {nonEmptyScopes.length === 0 && (
            <div className="text-sm opacity-50 py-2">
              {t("property.scopePreference.noValues")}
            </div>
          )}
        </div>

        <div className="flex justify-between gap-2 mt-1">
          <Button isDisabled={!preference} size="sm" variant="light" onPress={handleReset}>
            {t("property.scopePreference.reset")}
          </Button>
          <div className="flex gap-1">
            <Button size="sm" variant="light" onPress={() => setOpen(false)}>
              {t("property.scopePreference.cancel")}
            </Button>
            <Button color="primary" isDisabled={!dirty} size="sm" onPress={handleSave}>
              {t("property.scopePreference.save")}
            </Button>
          </div>
        </div>
      </div>
    </Popover>
  );
};

export default ScopePreferencePopover;
