"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BulkModificationScopePreferenceConfig } from "@/pages/bulk-modification/components/BulkModification/models";
import type { IProperty } from "@/components/Property/models";
import type { PropertyValueScopePriority } from "@/core/models/Resource";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  MdAdd,
  MdArrowDownward,
  MdArrowUpward,
  MdClose,
} from "react-icons/md";

import { Button, Card, CardBody, Modal, Switch } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertySelector from "@/components/PropertySelector";
import { PropertyLabel } from "@/components/Property";
import {
  PropertyPool,
  PropertyValueScope,
  PropertyValueScopeLabel,
  propertyValueScopes,
} from "@/sdk/constants";

type Props = {
  config?: Partial<BulkModificationScopePreferenceConfig>;
  onSubmit?: (config: BulkModificationScopePreferenceConfig) => void;
} & DestroyableProps;

const validate = (c: Partial<BulkModificationScopePreferenceConfig>) =>
  c.propertyPool !== undefined && c.propertyId !== undefined && c.property !== undefined;

const ScopePreferenceConfigModal = ({ onDestroyed, config: propsConfig, onSubmit }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [config, setConfig] = useState<Partial<BulkModificationScopePreferenceConfig>>(
    propsConfig ?? { priorities: [] },
  );

  const priorities = config.priorities ?? [];
  const priorityLen = priorities.length;

  const indexOfScope = (scope: PropertyValueScope) =>
    priorities.findIndex((p) => p.scope === scope);

  const addScope = (scope: PropertyValueScope) => {
    setConfig({
      ...config,
      priorities: [...priorities, { scope, fallbackOnEmpty: true }],
    });
  };

  const removeScope = (scope: PropertyValueScope) => {
    setConfig({
      ...config,
      priorities: priorities.filter((p) => p.scope !== scope),
    });
  };

  const moveScope = (scope: PropertyValueScope, dir: -1 | 1) => {
    const idx = priorities.findIndex((p) => p.scope === scope);

    if (idx < 0) return;
    const target = idx + dir;

    if (target < 0 || target >= priorityLen) return;
    const next = [...priorities];

    [next[idx], next[target]] = [next[target], next[idx]];
    setConfig({ ...config, priorities: next });
  };

  const setFallback = (scope: PropertyValueScope, fallbackOnEmpty: boolean) => {
    const idx = priorities.findIndex((p) => p.scope === scope);

    if (idx < 0) return;
    const next: PropertyValueScopePriority[] = [...priorities];

    next[idx] = { ...next[idx], fallbackOnEmpty };
    setConfig({ ...config, priorities: next });
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: { isDisabled: !validate(config) },
      }}
      size="xl"
      title={
        config.property
          ? t<string>("bulkModification.scopePreference.title.forProperty", { property: config.property.name })
          : t<string>("bulkModification.scopePreference.title.new")
      }
      onDestroyed={onDestroyed}
      onOk={() => {
        if (!validate(config)) {
          throw new Error("Invalid scope preference config");
        }
        onSubmit?.(config as BulkModificationScopePreferenceConfig);
      }}
    >
      <Card>
        <CardBody>
          <div className="grid items-center gap-2" style={{ gridTemplateColumns: "auto 1fr" }}>
            <div className="text-right">{t<string>("bulkModification.label.property")}</div>
            <div>
              <Button
                color="primary"
                size="sm"
                variant="light"
                onClick={() => {
                  createPortal(PropertySelector, {
                    v2: true,
                    pool: PropertyPool.Reserved | PropertyPool.Custom,
                    multiple: false,
                    onSubmit: async (ps: IProperty[]) => {
                      const p = ps[0];

                      setConfig({
                        ...config,
                        propertyPool: p.pool,
                        propertyId: p.id,
                        property: p,
                      });
                    },
                  });
                }}
              >
                {config.property ? (
                  <PropertyLabel showPool property={config.property} />
                ) : (
                  t<string>("bulkModification.select.property")
                )}
              </Button>
            </div>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardBody>
          <div className="text-xs opacity-60 mb-2">
            {t<string>("bulkModification.scopePreference.tip")}
          </div>
          <div className="flex flex-col gap-1">
            {(() => {
              const inListScopes = priorities.map((p) => p.scope);
              const inListSet = new Set(inListScopes);
              const restScopes = propertyValueScopes
                .map((s) => s.value as PropertyValueScope)
                .filter((s) => !inListSet.has(s));

              return [...inListScopes, ...restScopes];
            })().map((scope) => {
              const idx = indexOfScope(scope);
              const inList = idx >= 0;
              const isLast = inList && idx === priorityLen - 1;
              const entry = inList ? priorities[idx] : undefined;
              // A non-last entry with fallbackOnEmpty=false cuts off the chain; anything after it is inert.
              const cutoffIdx = priorities.findIndex(
                (p, i) => !p.fallbackOnEmpty && i < priorityLen - 1,
              );
              const isCutOff = inList && cutoffIdx >= 0 && idx > cutoffIdx;

              return (
                <div
                  className={`flex items-center gap-1 text-sm ${isCutOff ? "opacity-40" : ""}`}
                  key={scope}
                >
                  {inList ? (
                    <>
                      <span className="font-mono text-xs opacity-60 w-5 text-right">{idx + 1}.</span>
                      <Button
                        isIconOnly
                        isDisabled={isCutOff || idx === 0}
                        size="sm"
                        title={t<string>("property.scopePreference.moveUp")}
                        variant="light"
                        onPress={() => moveScope(scope, -1)}
                      >
                        <MdArrowUpward size={12} />
                      </Button>
                      <Button
                        isIconOnly
                        isDisabled={isCutOff || isLast}
                        size="sm"
                        title={t<string>("property.scopePreference.moveDown")}
                        variant="light"
                        onPress={() => moveScope(scope, 1)}
                      >
                        <MdArrowDownward size={12} />
                      </Button>
                      <Button
                        isIconOnly
                        isDisabled={isCutOff}
                        size="sm"
                        title={t<string>("property.scopePreference.removeFromList")}
                        variant="light"
                        onPress={() => removeScope(scope)}
                      >
                        <MdClose size={12} />
                      </Button>
                    </>
                  ) : (
                    <Button
                      isIconOnly
                      size="sm"
                      title={t<string>("property.scopePreference.addToList")}
                      variant="light"
                      onPress={() => addScope(scope)}
                    >
                      <MdAdd size={14} />
                    </Button>
                  )}
                  <span className={`flex-1 truncate ${inList ? "" : "opacity-60"}`}>
                    <span className="font-medium">{PropertyValueScopeLabel[scope]}</span>
                  </span>
                  {inList && (
                    <Switch
                      isDisabled={isCutOff || isLast}
                      isSelected={isLast ? true : entry!.fallbackOnEmpty}
                      size="sm"
                      title={
                        isCutOff
                          ? t<string>("property.scopePreference.cutOff")
                          : isLast
                            ? t<string>("property.scopePreference.fallbackDisabledOnLast")
                            : t<string>("property.scopePreference.fallbackOnEmpty")
                      }
                      onValueChange={(v) => setFallback(scope, v)}
                    />
                  )}
                </div>
              );
            })}
          </div>
        </CardBody>
      </Card>
    </Modal>
  );
};

ScopePreferenceConfigModal.displayName = "ScopePreferenceConfigModal";

export default ScopePreferenceConfigModal;
