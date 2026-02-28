"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineSetting, AiOutlineWarning, AiOutlineInfoCircle } from "react-icons/ai";

import {
  buildSourceStates,
  convertStatesToEnhancerOptions,
  extractEnhancerLevelConfigs,
  getGroupPropertyRows,
  getGroupEnabledCount,
  getGroupDynamicEnhancers,
  enhancerNeedsConfig,
  sourceKey,
  groupOrder,
  groupEnhancerMap,
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
  Tab,
  Tabs,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { EnhancerId } from "@/sdk/constants";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";
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
  const [activeGroup, setActiveGroup] = useState<PropertyGroup>(PropertyGroup.General);

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      const descs = (r.data || []) as EnhancerDescriptor[];
      setDescriptors(descs);
      const existingConfig = propEnhancerOptions ?? [];
      setSourceStates(buildSourceStates(descs, existingConfig));
      setEnhancerLevelConfigs(extractEnhancerLevelConfigs(existingConfig));
    });
  }, []);

  // Derived data for current group
  const currentProperties = useMemo(
    () => getGroupPropertyRows(activeGroup, sourceStates),
    [activeGroup, sourceStates]
  );

  const dynamicEnhancers = useMemo(
    () => getGroupDynamicEnhancers(activeGroup, descriptors),
    [activeGroup, descriptors]
  );

  // Toggle a single source
  const toggleSource = (stateKey: string) => {
    setSourceStates((prev) => {
      const next = new Map(prev);
      const state = next.get(stateKey);
      if (state) {
        next.set(stateKey, { ...state, enabled: !state.enabled });
      }
      return next;
    });
  };

  // Toggle all sources in the current group
  const toggleAllInGroup = (enabled: boolean) => {
    const enhancerIds = groupEnhancerMap[activeGroup];
    setSourceStates((prev) => {
      const next = new Map(prev);
      for (const [key, state] of next) {
        if (enhancerIds.includes(state.enhancerId)) {
          next.set(key, { ...state, enabled });
        }
      }
      return next;
    });
  };

  // Open enhancer config modal
  const openEnhancerConfig = (enhancer: EnhancerDescriptor) => {
    const currentLevelConfig = enhancerLevelConfigs.get(enhancer.id) ?? {};

    // Collect enabled targets for this enhancer from source states
    const enabledTargets: { target: number; dynamicTarget?: string; pool?: number; propertyId?: number; autoBindProperty?: boolean; autoMatchMultilevelString?: boolean }[] = [];
    for (const [, state] of sourceStates) {
      if (state.enhancerId === enhancer.id && state.enabled) {
        enabledTargets.push({
          target: state.targetId,
          dynamicTarget: state.isDynamic ? state.dynamicTarget : undefined,
          pool: state.targetMapping?.pool,
          propertyId: state.targetMapping?.id,
          autoBindProperty: state.config?.autoBindProperty,
          autoMatchMultilevelString: state.config?.autoMatchMultilevelString,
        });
      }
    }

    const optionsForModal: EnhancerFullOptions = {
      targetOptions: enabledTargets.map((et) => ({
        target: et.target,
        dynamicTarget: et.dynamicTarget,
        propertyPool: et.pool,
        propertyId: et.propertyId,
        autoBindProperty: et.autoBindProperty,
        autoMatchMultilevelString: et.autoMatchMultilevelString,
      })),
      expressions: currentLevelConfig.expressions ?? undefined,
      requirements: currentLevelConfig.requirements as EnhancerId[] ?? undefined,
      keywordProperty: currentLevelConfig.keywordProperty as EnhancerFullOptions["keywordProperty"],
      pretreatKeyword: currentLevelConfig.pretreatKeyword ?? undefined,
      bangumiPrioritySubjectType: currentLevelConfig.bangumiPrioritySubjectType ?? undefined,
    };

    createPortal(EnhancerOptionsModal, {
      enhancer,
      options: optionsForModal,
      onSubmit: async (newOptions: EnhancerFullOptions) => {
        // Update enhancer-level config
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

        // Reconcile source states with modal results
        if (newOptions.targetOptions) {
          setSourceStates((prev) => {
            const next = new Map(prev);

            // Find existing keys for this enhancer
            const existingDynamicKeys = new Set<string>();
            for (const [key, state] of next) {
              if (state.enhancerId === enhancer.id && state.isDynamic) {
                existingDynamicKeys.add(key);
              }
            }

            // Process returned target options
            const processedKeys = new Set<string>();
            for (const to of newOptions.targetOptions!) {
              const target = enhancer.targets.find((t) => t.id === to.target);
              if (!target) continue;

              const key = target.isDynamic
                ? sourceKey(enhancer.id, to.target, to.dynamicTarget)
                : sourceKey(enhancer.id, to.target);

              processedKeys.add(key);

              const existing = next.get(key);
              if (existing) {
                // Update existing source
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
                // New dynamic target
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

            // Remove dynamic targets that were removed in the modal
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

  // Stats for select-all checkbox
  const groupStats = useMemo(() => {
    const enhancerIds = groupEnhancerMap[activeGroup];
    let total = 0;
    let enabled = 0;
    for (const [, state] of sourceStates) {
      if (enhancerIds.includes(state.enhancerId)) {
        total++;
        if (state.enabled) enabled++;
      }
    }
    return { total, enabled };
  }, [activeGroup, sourceStates]);

  const allSelected = groupStats.enabled === groupStats.total && groupStats.total > 0;
  const someSelected = groupStats.enabled > 0;

  const renderPropertyRow = (prop: PropertyRow) => {
    const hasEnabled = prop.sources.some((s) => s.enabled);
    return (
      <div
        key={prop.propertyName}
        className={`flex items-center gap-3 py-1.5 px-2 rounded ${hasEnabled ? "bg-success-50" : ""}`}
      >
        <div className="min-w-[140px] text-sm font-medium truncate">
          {prop.propertyName}
        </div>
        <div className="flex items-center gap-2 flex-wrap">
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
        {/* ─── Fixed top: Group tabs ─── */}
        <Tabs
          selectedKey={activeGroup}
          onSelectionChange={(key) => setActiveGroup(key as PropertyGroup)}
          classNames={{
            base: "flex-shrink-0",
            tabList: "flex-wrap",
            panel: "hidden",
          }}
          size="sm"
          aria-label="Property groups"
        >
          {groupOrder.map((group) => {
            const count = getGroupEnabledCount(group, sourceStates);
            return (
              <Tab
                key={group}
                title={
                  <div className="flex items-center gap-1.5">
                    <span>{t(`enhancementConfig.scenario.${group}`)}</span>
                    {count > 0 && (
                      <Chip size="sm" variant="flat" color="primary" className="h-5 min-w-5">
                        {count}
                      </Chip>
                    )}
                  </div>
                }
              >
                {null}
              </Tab>
            );
          })}
        </Tabs>

        {/* ─── Scrollable middle: Property list ─── */}
        <div className="flex-1 overflow-y-auto py-2 min-h-0">
          {/* Select all */}
          {currentProperties.length > 0 && (
            <div className="flex items-center gap-2 mb-2 px-2">
              <Checkbox
                isIndeterminate={someSelected && !allSelected}
                isSelected={allSelected}
                size="sm"
                onValueChange={(checked) => toggleAllInGroup(checked)}
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

          {/* Property rows */}
          <div className="flex flex-col gap-0.5">
            {currentProperties.map((prop) => renderPropertyRow(prop))}
          </div>

          {currentProperties.length === 0 && (
            <div className="py-8 text-center text-sm text-default-400">
              {t("enhancementConfig.noStaticProperties")}
            </div>
          )}
        </div>

        {/* ─── Fixed bottom: Enhancer configuration ─── */}
        <div className="flex-shrink-0 border-t border-default-200 pt-3">
          {/* Dynamic property hint */}
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
              // Check if this enhancer has any enabled target
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
                      <AiOutlineSetting />
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
