"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";

import { useEffect, useMemo, useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineSetting, AiOutlineWarning, AiOutlineInfoCircle } from "react-icons/ai";

import {
  buildSourceStates,
  convertStatesToEnhancerOptions,
  extractEnhancerLevelConfigs,
  getGroupEnabledCount,
  getPropertyRowsForGroups,
  getDynamicEnhancersForGroups,
  getEnhancerIdsForGroups,
  enhancerNeedsConfig,
  getRowBinding,
  isRowEnabledButUnbound,
  sourceKey,
  groupOrder,
  PropertyGroup,
} from "./utils";
import type { PropertyRow, SourceState } from "./utils";

import {
  Button,
  Card,
  CardBody,
  Checkbox,
  Chip,
  Modal,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { EnhancerId, PropertyPool } from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import PropertyMatcher from "@/components/PropertyMatcher";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import EnhancerOptionsModal from "@/components/EnhancerSelectorV2/components/EnhancerOptionsModal";

type ApiEnhancerOptions = BakabaseAbstractionsModelsDomainEnhancerFullOptions;

type Props = {
  enhancerOptions?: ApiEnhancerOptions[];
  onSubmit?: (options: ApiEnhancerOptions[]) => any;
} & DestroyableProps;

const EnhancementConfigPanel = ({ enhancerOptions: propEnhancerOptions, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [descriptors, setDescriptors] = useState<EnhancerDescriptor[]>([]);
  const [sourceStates, setSourceStates] = useState<Map<string, SourceState>>(new Map());
  const [enhancerLevelConfigs, setEnhancerLevelConfigs] = useState<Map<EnhancerId, Partial<ApiEnhancerOptions>>>(new Map());
  const [selectedGroups, setSelectedGroups] = useState<Set<PropertyGroup>>(new Set([PropertyGroup.General]));
  // Cache of loaded properties for display in PropertyMatcher
  const [propertyCache, setPropertyCache] = useState<Map<string, IProperty>>(new Map());

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      const descs = (r.data || []) as EnhancerDescriptor[];
      setDescriptors(descs);
      const existingConfig = propEnhancerOptions ?? [];
      const states = buildSourceStates(descs, existingConfig);
      setSourceStates(states);
      setEnhancerLevelConfigs(extractEnhancerLevelConfigs(existingConfig));

      // Load bound properties into cache
      const propertyIds = new Map<PropertyPool, Set<number>>();
      for (const [, state] of states) {
        if (state.targetMapping) {
          const ids = propertyIds.get(state.targetMapping.pool) ?? new Set();
          ids.add(state.targetMapping.id);
          propertyIds.set(state.targetMapping.pool, ids);
        }
      }
      const pools = [...propertyIds.keys()];
      if (pools.length > 0) {
        Promise.all(
          pools.map((pool) => BApi.property.getPropertiesByPool(pool))
        ).then((results) => {
          const cache = new Map<string, IProperty>();
          for (const res of results) {
            for (const p of (res.data ?? []) as IProperty[]) {
              cache.set(`${p.pool}:${p.id}`, p);
            }
          }
          setPropertyCache(cache);
        });
      }
    });
  }, []);

  const selectedGroupsArray = useMemo(() => [...selectedGroups], [selectedGroups]);

  // Derived data for selected groups
  const currentProperties = useMemo(
    () => getPropertyRowsForGroups(selectedGroupsArray, sourceStates),
    [selectedGroupsArray, sourceStates]
  );

  const dynamicEnhancers = useMemo(
    () => getDynamicEnhancersForGroups(selectedGroupsArray, descriptors),
    [selectedGroupsArray, descriptors]
  );

  const selectedEnhancerIds = useMemo(
    () => getEnhancerIdsForGroups(selectedGroupsArray),
    [selectedGroupsArray]
  );

  // Toggle group selection
  const toggleGroup = useCallback((group: PropertyGroup) => {
    setSelectedGroups((prev) => {
      const next = new Set(prev);
      if (next.has(group)) {
        next.delete(group);
      } else {
        next.add(group);
      }
      return next;
    });
  }, []);

  // Toggle a single source
  const toggleSource = useCallback((stateKey: string) => {
    setSourceStates((prev) => {
      const next = new Map(prev);
      const state = next.get(stateKey);
      if (state) {
        next.set(stateKey, { ...state, enabled: !state.enabled });
      }
      return next;
    });
  }, []);

  // Toggle all sources in the selected groups
  const toggleAllInSelectedGroups = useCallback((enabled: boolean) => {
    setSourceStates((prev) => {
      const next = new Map(prev);
      for (const [key, state] of next) {
        if (selectedEnhancerIds.has(state.enhancerId)) {
          next.set(key, { ...state, enabled });
        }
      }
      return next;
    });
  }, [selectedEnhancerIds]);

  // Bind a property to all sources in a row
  const bindPropertyToRow = useCallback((row: PropertyRow, property: IProperty) => {
    setSourceStates((prev) => {
      const next = new Map(prev);
      for (const source of row.sources) {
        const state = next.get(source.stateKey);
        if (state) {
          next.set(source.stateKey, {
            ...state,
            targetMapping: { pool: property.pool, id: property.id },
          });
        }
      }
      return next;
    });
    setPropertyCache((prev) => {
      const next = new Map(prev);
      next.set(`${property.pool}:${property.id}`, property);
      return next;
    });
  }, []);

  // Unbind property from all sources in a row
  const unbindPropertyFromRow = useCallback((row: PropertyRow) => {
    setSourceStates((prev) => {
      const next = new Map(prev);
      for (const source of row.sources) {
        const state = next.get(source.stateKey);
        if (state) {
          next.set(source.stateKey, {
            ...state,
            targetMapping: undefined,
          });
        }
      }
      return next;
    });
  }, []);

  // Open enhancer config modal (only dynamic targets - static are configured outside)
  const openEnhancerConfig = (enhancer: EnhancerDescriptor) => {
    const currentLevelConfig = enhancerLevelConfigs.get(enhancer.id) ?? {};

    // Only collect dynamic enabled targets for the modal
    const dynamicTargets: { target: number; dynamicTarget?: string; pool?: number; propertyId?: number; autoBindProperty?: boolean; autoMatchMultilevelString?: boolean }[] = [];
    for (const [, state] of sourceStates) {
      if (state.enhancerId === enhancer.id && state.enabled && state.isDynamic) {
        dynamicTargets.push({
          target: state.targetId,
          dynamicTarget: state.dynamicTarget,
          pool: state.targetMapping?.pool,
          propertyId: state.targetMapping?.id,
          autoBindProperty: state.config?.autoBindProperty,
          autoMatchMultilevelString: state.config?.autoMatchMultilevelString,
        });
      }
    }

    // Pass a descriptor with only dynamic targets so the modal won't render FixedTargets
    const dynamicOnlyEnhancer: EnhancerDescriptor = {
      ...enhancer,
      targets: enhancer.targets.filter((t) => t.isDynamic),
    };

    const optionsForModal: EnhancerFullOptions = {
      targetOptions: dynamicTargets.map((dt) => ({
        target: dt.target,
        dynamicTarget: dt.dynamicTarget,
        propertyPool: dt.pool,
        propertyId: dt.propertyId,
        autoBindProperty: dt.autoBindProperty,
        autoMatchMultilevelString: dt.autoMatchMultilevelString,
      })),
      expressions: currentLevelConfig.expressions ?? undefined,
      requirements: currentLevelConfig.requirements as EnhancerId[] ?? undefined,
      keywordProperty: currentLevelConfig.keywordProperty as EnhancerFullOptions["keywordProperty"],
      pretreatKeyword: currentLevelConfig.pretreatKeyword ?? undefined,
      bangumiPrioritySubjectType: currentLevelConfig.bangumiPrioritySubjectType ?? undefined,
    };

    createPortal(EnhancerOptionsModal, {
      enhancer: dynamicOnlyEnhancer,
      options: optionsForModal,
      onSubmit: async (newOptions: EnhancerFullOptions) => {
        setEnhancerLevelConfigs((prev) => {
          const next = new Map(prev);
          next.set(enhancer.id, {
            enhancerId: enhancer.id,
            expressions: newOptions.expressions,
            requirements: newOptions.requirements,
            keywordProperty: newOptions.keywordProperty as ApiEnhancerOptions["keywordProperty"],
            pretreatKeyword: newOptions.pretreatKeyword,
            bangumiPrioritySubjectType: newOptions.bangumiPrioritySubjectType,
          });
          return next;
        });

        if (newOptions.targetOptions) {
          setSourceStates((prev) => {
            const next = new Map(prev);

            const existingDynamicKeys = new Set<string>();
            for (const [key, state] of next) {
              if (state.enhancerId === enhancer.id && state.isDynamic) {
                existingDynamicKeys.add(key);
              }
            }

            const processedKeys = new Set<string>();
            for (const to of newOptions.targetOptions!) {
              const target = enhancer.targets.find((tgt) => tgt.id === to.target);
              if (!target) continue;

              const key = target.isDynamic
                ? sourceKey(enhancer.id, to.target, to.dynamicTarget)
                : sourceKey(enhancer.id, to.target);

              processedKeys.add(key);

              const existing = next.get(key);
              if (existing) {
                next.set(key, {
                  ...existing,
                  targetMapping:
                    to.propertyPool != null && to.propertyId != null
                      ? { pool: to.propertyPool, id: to.propertyId }
                      : undefined,
                  config: {
                    autoBindProperty: to.autoBindProperty,
                    autoMatchMultilevelString: to.autoMatchMultilevelString,
                  },
                });
              } else if (target.isDynamic && to.dynamicTarget) {
                next.set(key, {
                  enhancerId: enhancer.id,
                  enhancerName: enhancer.name,
                  targetId: to.target,
                  targetDescriptor: target,
                  enabled: true,
                  isDynamic: true,
                  dynamicTarget: to.dynamicTarget,
                  targetMapping:
                    to.propertyPool != null && to.propertyId != null
                      ? { pool: to.propertyPool, id: to.propertyId }
                      : undefined,
                  config: {
                    autoBindProperty: to.autoBindProperty,
                    autoMatchMultilevelString: to.autoMatchMultilevelString,
                  },
                });
              }
            }

            for (const key of existingDynamicKeys) {
              if (!processedKeys.has(key)) {
                next.delete(key);
              }
            }

            return next;
          });
        }
      },
    });
  };

  const handleSubmit = () => {
    const result = convertStatesToEnhancerOptions(sourceStates, enhancerLevelConfigs);
    onSubmit?.(result);
  };

  // Stats for select-all checkbox across all selected groups
  const groupStats = useMemo(() => {
    let total = 0;
    let enabled = 0;
    for (const [, state] of sourceStates) {
      if (selectedEnhancerIds.has(state.enhancerId)) {
        total++;
        if (state.enabled) enabled++;
      }
    }
    return { total, enabled };
  }, [selectedEnhancerIds, sourceStates]);

  const allSelected = groupStats.enabled === groupStats.total && groupStats.total > 0;
  const someSelected = groupStats.enabled > 0;

  const renderPropertyRow = (prop: PropertyRow) => {
    const hasEnabled = prop.sources.some((s) => s.enabled);
    const binding = getRowBinding(prop, sourceStates);
    const unbound = isRowEnabledButUnbound(prop, sourceStates);
    const boundProperty = binding ? propertyCache.get(`${binding.pool}:${binding.id}`) : undefined;

    return (
      <div
        key={`${prop.propertyName}::${prop.propertyType}`}
        className={`flex items-center gap-3 py-1.5 px-2 rounded ${unbound ? "bg-warning-50" : hasEnabled ? "bg-success-50" : ""}`}
      >
        {/* Property name */}
        <div className="min-w-[120px] text-sm font-medium truncate">
          {prop.propertyName}
        </div>

        {/* Property type */}
        <div className="min-w-[100px] flex-shrink-0">
          <PropertyTypeIcon type={prop.propertyType} textVariant="default" />
        </div>

        {/* Enhancer checkboxes */}
        <div className="flex items-center gap-2 flex-wrap flex-1">
          {prop.sources.map((source) => {
            const desc = descriptors.find((d) => d.id === source.enhancerId);
            return (
              <Checkbox
                key={source.stateKey}
                isSelected={source.enabled}
                size="sm"
                onValueChange={() => toggleSource(source.stateKey)}
              >
                <span className="text-xs">
                  {desc ? (
                    <span className="flex items-center gap-1">
                      <BriefEnhancer enhancer={desc} />
                    </span>
                  ) : source.enhancerName}
                </span>
              </Checkbox>
            );
          })}
        </div>

        {/* Bound property */}
        <div className="flex items-center gap-1 flex-shrink-0">
          <PropertyMatcher
            matchedProperty={boundProperty}
            name={prop.propertyName}
            type={prop.propertyType}
            isClearable
            onValueChanged={(p) => {
              if (p) {
                bindPropertyToRow(prop, p);
              } else {
                unbindPropertyFromRow(prop);
              }
            }}
          />
          {unbound && (
            <Tooltip content={t<string>("enhancementConfig.unboundWarning")}>
              <AiOutlineWarning className="text-warning text-base flex-shrink-0" />
            </Tooltip>
          )}
        </div>
      </div>
    );
  };

  return (
    <Modal
      defaultVisible
      classNames={{ base: "max-w-[90vw] max-h-[90vh]" }}
      size="5xl"
      title={t<string>("enhancementConfig.title")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <div className="flex flex-col" style={{ height: "70vh" }}>
        {/* ─── Fixed top: Group tags (multi-select) ─── */}
        <div className="flex-shrink-0 flex items-center gap-2 pb-2 flex-wrap">
          <span className="text-xs text-default-400 whitespace-nowrap">
            {t("enhancementConfig.selectResourceType")}
          </span>
          {groupOrder.map((group) => {
            const isSelected = selectedGroups.has(group);
            const count = getGroupEnabledCount(group, sourceStates);
            return (
              <Chip
                key={group}
                className="cursor-pointer select-none"
                color={isSelected ? "primary" : "default"}
                variant={isSelected ? "solid" : "bordered"}
                size="sm"
                onClick={() => toggleGroup(group)}
              >
                {t(`enhancementConfig.scenario.${group}`)}
                {count > 0 && ` (${count})`}
              </Chip>
            );
          })}
        </div>

        {/* ─── Scrollable middle: Property list ─── */}
        <div className="flex-1 overflow-y-auto py-2 min-h-0">
          {selectedGroups.size > 0 && currentProperties.length > 0 && (
            <div className="flex items-center gap-2 mb-2 px-2">
              <Checkbox
                isIndeterminate={someSelected && !allSelected}
                isSelected={allSelected}
                size="sm"
                onValueChange={(checked) => toggleAllInSelectedGroups(checked)}
              >
                <span className="text-xs text-default-500">
                  {t("enhancementConfig.selectAll")}
                </span>
              </Checkbox>
              {someSelected && (
                <Chip size="sm" variant="flat" color="success">
                  {groupStats.enabled}/{groupStats.total}
                </Chip>
              )}
            </div>
          )}

          <div className="flex flex-col gap-0.5">
            {currentProperties.map((prop) => renderPropertyRow(prop))}
          </div>

          {selectedGroups.size === 0 && (
            <div className="py-8 text-center text-sm text-default-400">
              {t("enhancementConfig.selectGroupFirst")}
            </div>
          )}

          {selectedGroups.size > 0 && currentProperties.length === 0 && (
            <div className="py-8 text-center text-sm text-default-400">
              {t("enhancementConfig.noStaticProperties")}
            </div>
          )}
        </div>

        {/* ─── Fixed bottom: Enhancer configuration ─── */}
        <div className="flex-shrink-0 border-t border-default-200 pt-3">
          {dynamicEnhancers.length > 0 && (
            <div className="flex items-start gap-2 p-2.5 mb-2 bg-warning-50 rounded-lg text-sm">
              <AiOutlineInfoCircle className="text-warning mt-0.5 flex-shrink-0" />
              <div className="text-default-600">
                {t("enhancementConfig.dynamicHint", {
                  enhancers: dynamicEnhancers.map((e) => e.name).join(", "),
                })}
              </div>
            </div>
          )}

          <div className="text-xs font-medium text-default-500 mb-2">
            {t<string>("enhancementConfig.enhancerConfig")}
          </div>
          <div className="flex flex-wrap gap-2">
            {descriptors.map((enhancer) => {
              const needsCfg = enhancerNeedsConfig(enhancer, enhancerLevelConfigs.get(enhancer.id));
              let hasEnabled = false;
              for (const [, state] of sourceStates) {
                if (state.enhancerId === enhancer.id && state.enabled) {
                  hasEnabled = true;
                  break;
                }
              }

              return (
                <Card
                  key={enhancer.id}
                  className={`shadow-none border ${hasEnabled ? "border-primary-200" : "border-default-200"}`}
                >
                  <CardBody className="flex-row items-center gap-2 py-1.5 px-3">
                    <BriefEnhancer enhancer={enhancer} />
                    {needsCfg && (
                      <Chip size="sm" variant="flat" color="warning" startContent={<AiOutlineWarning />}>
                        {t<string>("enhancementConfig.needsConfig")}
                      </Chip>
                    )}
                    <Button
                      size="sm"
                      variant="light"
                      color="primary"
                      isIconOnly
                      onPress={() => openEnhancerConfig(enhancer)}
                    >
                      <AiOutlineSetting className="text-lg" />
                    </Button>
                  </CardBody>
                </Card>
              );
            })}
          </div>
        </div>
      </div>
    </Modal>
  );
};

EnhancementConfigPanel.displayName = "EnhancementConfigPanel";

export default EnhancementConfigPanel;
