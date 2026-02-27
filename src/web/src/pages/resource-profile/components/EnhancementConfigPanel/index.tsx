"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { EnhancerFullOptions } from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineSetting, AiOutlineWarning } from "react-icons/ai";

import {
  buildTargetItems,
  mergeWithExistingConfig,
  convertToEnhancerOptions,
  extractEnhancerLevelConfigs,
} from "./utils";
import type { EnhancementTargetItem } from "./utils";

import { Button, Card, CardBody, Checkbox, Chip, Divider, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { EnhancerId, EnhancerTag, PropertyPool } from "@/sdk/constants";
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
  const [targetItems, setTargetItems] = useState<Map<EnhancerId, EnhancementTargetItem[]>>(new Map());
  const [enhancerLevelConfigs, setEnhancerLevelConfigs] = useState<Map<EnhancerId, Partial<ApiEnhancerOptions>>>(new Map());

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      const descs = (r.data || []) as EnhancerDescriptor[];
      setDescriptors(descs);

      const items = buildTargetItems(descs);
      const existingConfig = propEnhancerOptions ?? [];
      const merged = mergeWithExistingConfig(items, existingConfig);
      setTargetItems(merged);
      setEnhancerLevelConfigs(extractEnhancerLevelConfigs(existingConfig));
    });
  }, []);

  const toggleTarget = (enhancerId: EnhancerId, targetId: number, dynamicTarget?: string) => {
    setTargetItems((prev) => {
      const next = new Map(prev);
      const targets = [...(next.get(enhancerId) ?? [])];
      const idx = targets.findIndex((t) =>
        t.targetId === targetId && t.dynamicTarget === dynamicTarget
      );
      if (idx !== -1) {
        targets[idx] = { ...targets[idx], enabled: !targets[idx].enabled };
        next.set(enhancerId, targets);
      }
      return next;
    });
  };

  const toggleAllForEnhancer = (enhancerId: EnhancerId, enabled: boolean) => {
    setTargetItems((prev) => {
      const next = new Map(prev);
      const targets = (next.get(enhancerId) ?? []).map((t) => ({ ...t, enabled }));
      next.set(enhancerId, targets);
      return next;
    });
  };

  const getEnhancerStats = (enhancerId: EnhancerId) => {
    const targets = targetItems.get(enhancerId) ?? [];
    const enabledCount = targets.filter((t) => t.enabled).length;
    return { total: targets.length, enabled: enabledCount };
  };

  const usedEnhancerIds = useMemo(() => {
    const ids: EnhancerId[] = [];
    for (const [enhancerId, targets] of targetItems) {
      if (targets.some((t) => t.enabled)) {
        ids.push(enhancerId);
      }
    }
    return ids;
  }, [targetItems]);

  const openEnhancerConfig = (enhancer: EnhancerDescriptor) => {
    const currentLevelConfig = enhancerLevelConfigs.get(enhancer.id) ?? {};
    const currentTargets = targetItems.get(enhancer.id) ?? [];
    const enabledTargets = currentTargets.filter((t) => t.enabled);

    // Build the options object that EnhancerOptionsModal expects
    const optionsForModal: EnhancerFullOptions = {
      targetOptions: enabledTargets.map((t) => ({
        target: t.targetId,
        dynamicTarget: t.dynamicTarget,
        propertyPool: t.targetMapping?.pool,
        propertyId: t.targetMapping?.id,
        autoBindProperty: t.config?.autoBindProperty,
        autoMatchMultilevelString: t.config?.autoMatchMultilevelString,
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
          setTargetItems((prev) => {
            const next = new Map(prev);
            const targets = [...(next.get(enhancer.id) ?? [])];
            for (const to of newOptions.targetOptions!) {
              const idx = targets.findIndex((t) =>
                t.targetId === to.target && t.dynamicTarget === to.dynamicTarget
              );
              if (idx !== -1) {
                targets[idx] = {
                  ...targets[idx],
                  targetMapping:
                    to.propertyPool != null && to.propertyId != null
                      ? { pool: to.propertyPool, id: to.propertyId }
                      : undefined,
                  config: {
                    autoBindProperty: to.autoBindProperty,
                    autoMatchMultilevelString: to.autoMatchMultilevelString,
                  },
                };
              }
            }
            next.set(enhancer.id, targets);
            return next;
          });
        }
      },
    });
  };

  const needsConfig = (enhancerId: EnhancerId): boolean => {
    const desc = descriptors.find((d) => d.id === enhancerId);
    if (!desc) return false;
    const levelConfig = enhancerLevelConfigs.get(enhancerId);

    // Regex needs expressions
    if (desc.tags.includes(EnhancerTag.UseRegex)) {
      if (!levelConfig?.expressions || levelConfig.expressions.filter((e) => e.trim()).length === 0) {
        return true;
      }
    }
    return false;
  };

  const handleSubmit = () => {
    const result = convertToEnhancerOptions(targetItems, enhancerLevelConfigs);
    onSubmit?.(result);
  };

  return (
    <Modal
      defaultVisible
      classNames={{ base: "max-w-[90vw] max-h-[90vh]" }}
      size="5xl"
      title={t<string>("resourceProfile.enhancementConfig.title")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <div className="flex flex-col gap-4 overflow-auto max-h-[70vh]">
        {/* Target Selection Section */}
        <div>
          <div className="text-base font-medium mb-2">
            {t<string>("resourceProfile.enhancementConfig.targetSelection")}
          </div>
          <div className="flex flex-col gap-3">
            {[...targetItems.entries()].map(([enhancerId, targets]) => {
              const desc = descriptors.find((d) => d.id === enhancerId);
              if (!desc) return null;
              const stats = getEnhancerStats(enhancerId);
              const allSelected = stats.enabled === stats.total && stats.total > 0;
              const someSelected = stats.enabled > 0;

              return (
                <div key={enhancerId} className="border border-default-200 rounded-lg p-3">
                  {/* Enhancer header */}
                  <div className="flex items-center gap-2 mb-2">
                    <Checkbox
                      isIndeterminate={someSelected && !allSelected}
                      isSelected={allSelected}
                      size="sm"
                      onValueChange={(checked) => toggleAllForEnhancer(enhancerId, checked)}
                    />
                    <BriefEnhancer enhancer={desc} />
                    {someSelected && (
                      <Chip size="sm" variant="flat" color="success">
                        {stats.enabled}/{stats.total}
                      </Chip>
                    )}
                  </div>

                  {/* Target grid */}
                  <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 gap-1 pl-7">
                    {targets.map((target) => (
                      <div
                        key={`${target.targetId}-${target.dynamicTarget ?? ""}`}
                        className="flex items-center gap-1"
                      >
                        <Checkbox
                          isSelected={target.enabled}
                          size="sm"
                          onValueChange={() =>
                            toggleTarget(enhancerId, target.targetId, target.dynamicTarget)
                          }
                        >
                          <span className="text-sm">{target.targetName}</span>
                        </Checkbox>
                        {target.isDynamic && (
                          <Chip size="sm" variant="flat" color="warning" className="h-4 text-[10px]">
                            {t<string>("resourceProfile.enhancementConfig.dynamic")}
                          </Chip>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Enhancer Configuration Section */}
        {usedEnhancerIds.length > 0 && (
          <>
            <Divider />
            <div>
              <div className="text-base font-medium mb-2">
                {t<string>("resourceProfile.enhancementConfig.enhancerConfig")}
              </div>
              <div className="flex flex-col gap-2">
                {usedEnhancerIds.map((enhancerId) => {
                  const desc = descriptors.find((d) => d.id === enhancerId);
                  if (!desc) return null;
                  const stats = getEnhancerStats(enhancerId);
                  const needsCfg = needsConfig(enhancerId);

                  return (
                    <Card key={enhancerId} className="shadow-none border border-default-200">
                      <CardBody className="flex-row items-center justify-between py-2 px-3">
                        <div className="flex items-center gap-2">
                          <BriefEnhancer enhancer={desc} />
                          {needsCfg ? (
                            <Chip size="sm" variant="flat" color="warning" startContent={<AiOutlineWarning />}>
                              {t<string>("resourceProfile.enhancementConfig.needsConfig")}
                            </Chip>
                          ) : (
                            <Chip size="sm" variant="flat" color="success" startContent={<AiOutlineCheckCircle />}>
                              {t<string>("resourceProfile.enhancementConfig.configured")}
                            </Chip>
                          )}
                          <span className="text-xs text-default-400">
                            {t("resourceProfile.enhancementConfig.targetsUsing", { count: stats.enabled })}
                          </span>
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
