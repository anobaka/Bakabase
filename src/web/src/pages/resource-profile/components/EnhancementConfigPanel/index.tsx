"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineSetting, AiOutlineWarning, AiOutlineInfoCircle } from "react-icons/ai";

import {
  buildScenarioGroups,
  convertGroupsToEnhancerOptions,
  extractEnhancerLevelConfigs,
  getUsedEnhancerIds,
  enhancerNeedsConfig,
} from "./utils";
import type { ScenarioGroup, PropertyRow } from "./utils";

import {
  Button,
  Card,
  CardBody,
  Checkbox,
  Chip,
  Divider,
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
  const [groups, setGroups] = useState<ScenarioGroup[]>([]);
  const [enhancerLevelConfigs, setEnhancerLevelConfigs] = useState<Map<EnhancerId, Partial<ApiEnhancerOptions>>>(new Map());

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      const descs = (r.data || []) as EnhancerDescriptor[];
      setDescriptors(descs);

      const existingConfig = propEnhancerOptions ?? [];
      const scenarioGroups = buildScenarioGroups(descs, existingConfig);
      setGroups(scenarioGroups);
      setEnhancerLevelConfigs(extractEnhancerLevelConfigs(existingConfig));
    });
  }, []);

  const toggleSource = (
    scenarioIdx: number,
    isDynamic: boolean,
    propIdx: number,
    sourceIdx: number
  ) => {
    setGroups((prev) => {
      const next = [...prev];
      const group = { ...next[scenarioIdx] };
      const propList = isDynamic
        ? [...group.dynamicProperties]
        : [...group.staticProperties];
      const prop = { ...propList[propIdx] };
      const sources = [...prop.sources];
      sources[sourceIdx] = { ...sources[sourceIdx], enabled: !sources[sourceIdx].enabled };
      prop.sources = sources;
      propList[propIdx] = prop;
      if (isDynamic) {
        group.dynamicProperties = propList;
      } else {
        group.staticProperties = propList;
      }
      next[scenarioIdx] = group;
      return next;
    });
  };

  const toggleAllInScenario = (scenarioIdx: number, isDynamic: boolean, enabled: boolean) => {
    setGroups((prev) => {
      const next = [...prev];
      const group = { ...next[scenarioIdx] };
      const propList = (isDynamic ? group.dynamicProperties : group.staticProperties).map((prop) => ({
        ...prop,
        sources: prop.sources.map((s) => ({ ...s, enabled })),
      }));
      if (isDynamic) {
        group.dynamicProperties = propList;
      } else {
        group.staticProperties = propList;
      }
      next[scenarioIdx] = group;
      return next;
    });
  };

  const usedEnhancerIds = useMemo(() => getUsedEnhancerIds(groups), [groups]);

  const openEnhancerConfig = (enhancer: EnhancerDescriptor) => {
    const currentLevelConfig = enhancerLevelConfigs.get(enhancer.id) ?? {};

    // Collect enabled targets for this enhancer across all groups
    const enabledTargets: { target: number; dynamicTarget?: string; pool?: number; propertyId?: number; autoBindProperty?: boolean; autoMatchMultilevelString?: boolean }[] = [];
    for (const group of groups) {
      for (const prop of [...group.staticProperties, ...group.dynamicProperties]) {
        for (const source of prop.sources) {
          if (source.enhancerId === enhancer.id && source.enabled) {
            enabledTargets.push({
              target: source.targetId,
              dynamicTarget: source.targetDescriptor.isDynamic ? prop.propertyName : undefined,
              pool: source.targetMapping?.pool,
              propertyId: source.targetMapping?.id,
              autoBindProperty: source.config?.autoBindProperty,
              autoMatchMultilevelString: source.config?.autoMatchMultilevelString,
            });
          }
        }
      }
    }

    const optionsForModal: EnhancerFullOptions = {
      targetOptions: enabledTargets.map((t) => ({
        target: t.target,
        dynamicTarget: t.dynamicTarget,
        propertyPool: t.pool,
        propertyId: t.propertyId,
        autoBindProperty: t.autoBindProperty,
        autoMatchMultilevelString: t.autoMatchMultilevelString,
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

        // Update target-level config from modal results
        if (newOptions.targetOptions) {
          setGroups((prev) => {
            const next = prev.map((group) => {
              const updatePropList = (propList: PropertyRow[]): PropertyRow[] =>
                propList.map((prop) => ({
                  ...prop,
                  sources: prop.sources.map((source) => {
                    if (source.enhancerId !== enhancer.id) return source;
                    const to = newOptions.targetOptions?.find((t) =>
                      t.target === source.targetId &&
                      (source.targetDescriptor.isDynamic
                        ? t.dynamicTarget === prop.propertyName
                        : true)
                    );
                    if (!to) return source;
                    return {
                      ...source,
                      targetMapping:
                        to.propertyPool != null && to.propertyId != null
                          ? { pool: to.propertyPool, id: to.propertyId }
                          : undefined,
                      config: {
                        autoBindProperty: to.autoBindProperty,
                        autoMatchMultilevelString: to.autoMatchMultilevelString,
                      },
                    };
                  }),
                }));

              return {
                ...group,
                staticProperties: updatePropList(group.staticProperties),
                dynamicProperties: updatePropList(group.dynamicProperties),
              };
            });
            return next;
          });
        }
      },
    });
  };

  const handleSubmit = () => {
    const result = convertGroupsToEnhancerOptions(groups, enhancerLevelConfigs);
    onSubmit?.(result);
  };

  const getScenarioStats = (group: ScenarioGroup, isDynamic: boolean) => {
    const props = isDynamic ? group.dynamicProperties : group.staticProperties;
    let total = 0;
    let enabled = 0;
    for (const prop of props) {
      for (const source of prop.sources) {
        total++;
        if (source.enabled) enabled++;
      }
    }
    return { total, enabled };
  };

  const renderPropertyRow = (
    prop: PropertyRow,
    scenarioIdx: number,
    propIdx: number,
    isDynamic: boolean
  ) => {
    const hasEnabled = prop.sources.some((s) => s.enabled);
    return (
      <div
        key={`${prop.propertyName}-${propIdx}`}
        className={`flex items-center gap-3 py-1.5 px-2 rounded ${hasEnabled ? "bg-success-50" : ""}`}
      >
        <div className="min-w-[140px] text-sm font-medium truncate">
          {prop.propertyName}
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          {prop.sources.map((source, sourceIdx) => {
            const desc = descriptors.find((d) => d.id === source.enhancerId);
            return (
              <Checkbox
                key={`${source.enhancerId}-${source.targetId}`}
                isSelected={source.enabled}
                size="sm"
                onValueChange={() => toggleSource(scenarioIdx, isDynamic, propIdx, sourceIdx)}
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

  const renderScenarioGroup = (group: ScenarioGroup, scenarioIdx: number) => {
    const hasDynamicEnhancers = group.dynamicEnhancers.length > 0;

    return (
      <div key={group.scenario} className="border border-default-200 rounded-lg">
        {/* Scenario header */}
        <div className="flex items-center gap-2 px-3 py-2 bg-default-50 rounded-t-lg border-b border-default-200">
          <span className="text-sm font-semibold">
            {t(`enhancementConfig.scenario.${group.scenario}`)}
          </span>
          <span className="text-xs text-default-400">
            {group.enhancers.map((e) => e.name).join(", ")}
          </span>
        </div>

        <div className="p-2">
          {hasDynamicEnhancers ? (
            <Tabs
              size="sm"
              aria-label={`${group.scenario} tabs`}
              classNames={{ panel: "py-2" }}
            >
              <Tab key="static" title={t<string>("enhancementConfig.tab.staticProperties")}>
                {renderStaticTab(group, scenarioIdx)}
              </Tab>
              <Tab key="dynamic" title={t<string>("enhancementConfig.tab.dynamicProperties")}>
                {renderDynamicTab(group, scenarioIdx)}
              </Tab>
            </Tabs>
          ) : (
            renderStaticTab(group, scenarioIdx)
          )}
        </div>
      </div>
    );
  };

  const renderStaticTab = (group: ScenarioGroup, scenarioIdx: number) => {
    const stats = getScenarioStats(group, false);
    const allSelected = stats.enabled === stats.total && stats.total > 0;
    const someSelected = stats.enabled > 0;

    return (
      <div className="flex flex-col gap-1">
        {group.staticProperties.length > 0 && (
          <div className="flex items-center gap-2 mb-1">
            <Checkbox
              isIndeterminate={someSelected && !allSelected}
              isSelected={allSelected}
              size="sm"
              onValueChange={(checked) => toggleAllInScenario(scenarioIdx, false, checked)}
            >
              <span className="text-xs text-default-500">
                {t("enhancementConfig.selectAll")}
              </span>
            </Checkbox>
            {someSelected && (
              <Chip size="sm" variant="flat" color="success">
                {stats.enabled}/{stats.total}
              </Chip>
            )}
          </div>
        )}
        {group.staticProperties.map((prop, propIdx) =>
          renderPropertyRow(prop, scenarioIdx, propIdx, false)
        )}
        {group.staticProperties.length === 0 && (
          <div className="py-4 text-center text-sm text-default-400">
            {t("enhancementConfig.noStaticProperties")}
          </div>
        )}
      </div>
    );
  };

  const renderDynamicTab = (group: ScenarioGroup, scenarioIdx: number) => {
    return (
      <div className="flex flex-col gap-3">
        {/* Explanation */}
        <div className="flex items-start gap-2 p-3 bg-warning-50 rounded-lg text-sm">
          <AiOutlineInfoCircle className="text-warning mt-0.5 flex-shrink-0" />
          <div className="text-default-600">
            {t("enhancementConfig.dynamicExplanation")}
          </div>
        </div>

        {/* Enhancer config entry points */}
        <div className="flex flex-col gap-2">
          <div className="text-xs font-medium text-default-500">
            {t("enhancementConfig.configureDynamicEnhancers")}
          </div>
          {group.dynamicEnhancers.map((enhancer) => {
            const needsCfg = enhancerNeedsConfig(enhancer, enhancerLevelConfigs.get(enhancer.id));
            return (
              <Card key={enhancer.id} className="shadow-none border border-default-200">
                <CardBody className="flex-row items-center justify-between py-2 px-3">
                  <div className="flex items-center gap-2">
                    <BriefEnhancer enhancer={enhancer} />
                    {needsCfg ? (
                      <Chip size="sm" variant="flat" color="warning" startContent={<AiOutlineWarning />}>
                        {t<string>("enhancementConfig.needsConfig")}
                      </Chip>
                    ) : (
                      <Chip size="sm" variant="flat" color="success" startContent={<AiOutlineCheckCircle />}>
                        {t<string>("enhancementConfig.configured")}
                      </Chip>
                    )}
                  </div>
                  <Button
                    size="sm"
                    variant="flat"
                    color="primary"
                    startContent={<AiOutlineSetting />}
                    onPress={() => openEnhancerConfig(enhancer)}
                  >
                    {t<string>("resourceProfile.action.configure")}
                  </Button>
                </CardBody>
              </Card>
            );
          })}
        </div>

        {/* Configured dynamic properties */}
        {group.dynamicProperties.length > 0 && (
          <div className="flex flex-col gap-1">
            <div className="text-xs font-medium text-default-500">
              {t("enhancementConfig.configuredDynamicProperties")}
            </div>
            {group.dynamicProperties.map((prop, propIdx) =>
              renderPropertyRow(prop, scenarioIdx, propIdx, true)
            )}
          </div>
        )}

        {group.dynamicProperties.length === 0 && (
          <div className="py-3 text-center text-sm text-default-400">
            {t("enhancementConfig.noDynamicPropertiesYet")}
          </div>
        )}
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
      <div className="flex flex-col gap-4 overflow-auto max-h-[70vh]">
        {/* Scenario groups */}
        {groups.map((group, idx) => renderScenarioGroup(group, idx))}

        {/* Enhancer Configuration Section */}
        {usedEnhancerIds.length > 0 && (
          <>
            <Divider />
            <div>
              <div className="text-base font-medium mb-2">
                {t<string>("enhancementConfig.enhancerConfig")}
              </div>
              <div className="flex flex-col gap-2">
                {usedEnhancerIds.map((enhancerId) => {
                  const desc = descriptors.find((d) => d.id === enhancerId);
                  if (!desc) return null;
                  const needsCfg = enhancerNeedsConfig(desc, enhancerLevelConfigs.get(enhancerId));

                  return (
                    <Card key={enhancerId} className="shadow-none border border-default-200">
                      <CardBody className="flex-row items-center justify-between py-2 px-3">
                        <div className="flex items-center gap-2">
                          <BriefEnhancer enhancer={desc} />
                          {needsCfg ? (
                            <Chip size="sm" variant="flat" color="warning" startContent={<AiOutlineWarning />}>
                              {t<string>("enhancementConfig.needsConfig")}
                            </Chip>
                          ) : (
                            <Chip size="sm" variant="flat" color="success" startContent={<AiOutlineCheckCircle />}>
                              {t<string>("enhancementConfig.configured")}
                            </Chip>
                          )}
                        </div>
                        <Button
                          size="sm"
                          variant="flat"
                          color="primary"
                          startContent={<AiOutlineSetting />}
                          onPress={() => openEnhancerConfig(desc)}
                        >
                          {t<string>("resourceProfile.action.configure")}
                        </Button>
                      </CardBody>
                    </Card>
                  );
                })}
              </div>
            </div>
          </>
        )}
      </div>
    </Modal>
  );
};

EnhancementConfigPanel.displayName = "EnhancementConfigPanel";

export default EnhancementConfigPanel;
