"use client";

import type { BakabaseServiceModelsViewComparisonPlanViewModel } from "@/sdk/Api";

import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  PlayCircleOutlined,
  CopyOutlined,
  ClearOutlined,
} from "@ant-design/icons";
import { useCallback, useEffect, useRef, useState } from "react";
import toast from "react-hot-toast";
import { AiOutlineInbox } from "react-icons/ai";

import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Input,
  Modal,
  Spinner,
  Tooltip,
  Progress,
  NumberInput,
  Card,
  CardBody,
  CardHeader,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";
import BetaChip from "@/components/Chips/BetaChip";
import ComparisonResults from "./components/ComparisonResults";
import RuleModal from "./components/RuleModal";
import type { RuleWithProperty } from "./components/RuleModal";
import RuleDemonstrator from "./components/RuleDemonstrator";
import ScoringExplanation from "./components/ScoringExplanation";
import { ResourceFilterController } from "@/components/ResourceFilter";
import { FilterDisplayMode, PropertyPool } from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";
import { EditableValue } from "@/components/EditableValue";

type ComparisonPlan = BakabaseServiceModelsViewComparisonPlanViewModel;

const ComparisonPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const bTasks = useBTasksStore((state) => state.tasks);

  const [expandedKeys, setExpandedKeys] = useState<string[]>([]);
  const [plans, setPlans] = useState<ComparisonPlan[]>();
  const isFirstLoad = useRef(true);

  // Helper to get task status for a plan from BTask store
  const getTaskForPlan = useCallback((planId: number) => {
    const taskId = `Comparison:${planId}`;
    return bTasks.find((t) => t.id === taskId);
  }, [bTasks]);

  // Rule modal state
  const [ruleModalOpen, setRuleModalOpen] = useState(false);
  const [editingRule, setEditingRule] = useState<RuleWithProperty | undefined>();
  const [editingRuleIndex, setEditingRuleIndex] = useState<number>(-1);
  const [editingPlanId, setEditingPlanId] = useState<number | null>(null);
  const [planRulesWithProperties, setPlanRulesWithProperties] = useState<Map<number, RuleWithProperty[]>>(new Map());

  const loadAllPlans = useCallback(async () => {
    const r = await BApi.comparison.getAllComparisonPlans();
    const items = r.data || [];

    setPlans(items);
    if (isFirstLoad.current) {
      setExpandedKeys(items.map((p) => p.id!.toString()));
      isFirstLoad.current = false;
    }
  }, []);

  useEffect(() => {
    loadAllPlans();
  }, []);

  // Reload plans when any comparison task completes to get updated lastRunAt and resultGroupCount
  const prevTasksRef = useRef<Map<string, number>>(new Map());
  useEffect(() => {
    const comparisonTasks = bTasks.filter((t) => t.id.startsWith("Comparison:"));
    const currentStatuses = new Map(comparisonTasks.map((t) => [t.id, t.status]));
    const prevStatuses = prevTasksRef.current;

    // Check if any task transitioned to a terminal state (Completed, Error, Cancelled)
    const terminalStates = [BTaskStatus.Completed, BTaskStatus.Error, BTaskStatus.Cancelled];
    let shouldReload = false;

    for (const [taskId, status] of currentStatuses) {
      const prevStatus = prevStatuses.get(taskId);
      if (prevStatus !== undefined && prevStatus !== status && terminalStates.includes(status)) {
        shouldReload = true;
        break;
      }
    }

    prevTasksRef.current = currentStatuses;

    if (shouldReload) {
      loadAllPlans();
    }
  }, [bTasks, loadAllPlans]);

  const handleAdd = () => {
    let nameInput = "";

    createPortal(Modal, {
      defaultVisible: true,
      size: "md",
      title: t("comparison.action.createPlan"),
      onOk: async () => {
        if (!nameInput.trim()) {
          toast.error(t("comparison.error.nameRequired"));
          throw new Error(t("comparison.error.nameRequired"));
        }
        const r = await BApi.comparison.createComparisonPlan({
          name: nameInput.trim(),
          threshold: 80,
          rules: [],
        });
        await loadAllPlans();
        if (r.data?.id) {
          setExpandedKeys((prev) => [...prev, r.data!.id!.toString()]);
        }
        toast.success(t("common.success.saved"));
      },
      children: (
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1">
            <Input
              label={t("comparison.label.name")}
              autoFocus
              isRequired
              defaultValue=""
              onValueChange={(v) => (nameInput = v)}
            />
          </div>
          <div className="text-sm text-default-400">
            {t("comparison.hint.configureRulesAfterCreation")}
          </div>
        </div>
      ),
    });
  };

  const handleEditBasicInfo = (plan: ComparisonPlan) => {
    let nameInput = plan.name || "";

    createPortal(Modal, {
      defaultVisible: true,
      size: "md",
      title: t("comparison.action.editPlan"),
      onOk: async () => {
        if (!nameInput.trim()) {
          toast.error(t("comparison.error.nameRequired"));
          throw new Error(t("comparison.error.nameRequired"));
        }
        await BApi.comparison.updateComparisonPlan(plan.id!, {
          name: nameInput.trim(),
        });
        await loadAllPlans();
        toast.success(t("common.success.saved"));
      },
      children: (
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1">
            <Input
              label={t("comparison.label.name")}
              autoFocus
              defaultValue={nameInput}
              isRequired
              onValueChange={(v) => (nameInput = v)}
            />
          </div>
        </div>
      ),
    });
  };

  // Load rules with property info for a plan
  const loadRulesWithProperties = useCallback(async (plan: ComparisonPlan) => {
    if (!plan.rules || plan.rules.length === 0) {
      setPlanRulesWithProperties((prev) => {
        const newMap = new Map(prev);
        newMap.set(plan.id!, []);
        return newMap;
      });
      return [];
    }

    const allPropertiesResponse = await BApi.property.getPropertiesByPool(PropertyPool.All);
    const allProperties = (allPropertiesResponse.data || []) as IProperty[];

    const rulesWithProperties: RuleWithProperty[] = plan.rules.map((r) => {
      const property = allProperties.find(
        (p) => p.id === r.propertyId && p.pool === r.propertyPool
      );
      return {
        order: r.order!,
        propertyPool: r.propertyPool!,
        propertyId: r.propertyId!,
        propertyValueScope: r.propertyValueScope,
        mode: r.mode!,
        normalize: (r as any).normalize ?? false,
        parameter: r.parameter,
        weight: r.weight!,
        isVeto: r.isVeto!,
        vetoThreshold: r.vetoThreshold!,
        oneNullBehavior: r.oneNullBehavior!,
        bothNullBehavior: r.bothNullBehavior!,
        property,
      };
    });

    setPlanRulesWithProperties((prev) => {
      const newMap = new Map(prev);
      newMap.set(plan.id!, rulesWithProperties);
      return newMap;
    });
    return rulesWithProperties;
  }, []);

  // Open rule modal to add a new rule
  const handleAddRule = (plan: ComparisonPlan) => {
    setEditingPlanId(plan.id!);
    setEditingRule(undefined);
    setEditingRuleIndex(-1);
    setRuleModalOpen(true);
  };

  // Open rule modal to edit an existing rule
  const handleEditRule = (plan: ComparisonPlan, rule: RuleWithProperty, index: number) => {
    setEditingPlanId(plan.id!);
    setEditingRule(rule);
    setEditingRuleIndex(index);
    setRuleModalOpen(true);
  };

  // Save rule (add or update)
  const handleSaveRule = async (rule: RuleWithProperty) => {
    if (!editingPlanId) return;

    const plan = plans?.find((p) => p.id === editingPlanId);
    if (!plan) return;

    const currentRules = planRulesWithProperties.get(editingPlanId) || [];
    let newRules: RuleWithProperty[];

    if (editingRuleIndex >= 0) {
      // Update existing rule
      newRules = [...currentRules];
      newRules[editingRuleIndex] = { ...rule, order: editingRuleIndex };
    } else {
      // Add new rule
      newRules = [...currentRules, { ...rule, order: currentRules.length }];
    }

    // Convert to input model (remove property field)
    const inputRules = newRules.map(({ property, ...rest }) => rest);

    await BApi.comparison.updateComparisonPlan(plan.id!, {
      name: plan.name,
      threshold: plan.threshold,
      rules: inputRules,
      search: plan.search,
    });

    await loadAllPlans();
    // Reload rules with properties
    const updatedPlan = (await BApi.comparison.getAllComparisonPlans()).data?.find((p) => p.id === editingPlanId);
    if (updatedPlan) {
      await loadRulesWithProperties(updatedPlan as ComparisonPlan);
    }
    toast.success(t("common.success.saved"));
  };

  // Delete a rule
  const handleDeleteRule = async (plan: ComparisonPlan, index: number) => {
    const currentRules = planRulesWithProperties.get(plan.id!) || [];
    const newRules = currentRules.filter((_, i) => i !== index);
    // Reorder
    newRules.forEach((r, i) => (r.order = i));

    // Convert to input model
    const inputRules = newRules.map(({ property, ...rest }) => rest);

    await BApi.comparison.updateComparisonPlan(plan.id!, {
      name: plan.name,
      threshold: plan.threshold,
      rules: inputRules,
      search: plan.search,
    });

    await loadAllPlans();
    // Reload rules with properties
    const updatedPlan = (await BApi.comparison.getAllComparisonPlans()).data?.find((p) => p.id === plan.id);
    if (updatedPlan) {
      await loadRulesWithProperties(updatedPlan as ComparisonPlan);
    }
    toast.success(t("common.success.saved"));
  };

  // Update filters inline (like BulkModification)
  const handleFilterChange = useCallback(async (plan: ComparisonPlan, updatedSearch: any) => {
    // Update local state immediately
    setPlans((prev) =>
      prev?.map((p) =>
        p.id === plan.id ? { ...p, search: updatedSearch } : p
      )
    );
    // Save to server
    await BApi.comparison.updateComparisonPlan(plan.id!, {
      name: plan.name,
      threshold: plan.threshold,
      rules: plan.rules || [],
      search: updatedSearch,
    });
  }, []);

  // Update threshold inline
  const handleThresholdChange = useCallback(async (plan: ComparisonPlan, newThreshold: number) => {
    // Update local state immediately
    setPlans((prev) =>
      prev?.map((p) =>
        p.id === plan.id ? { ...p, threshold: newThreshold } : p
      )
    );
    // Save to server
    await BApi.comparison.updateComparisonPlan(plan.id!, {
      name: plan.name,
      threshold: newThreshold,
      rules: plan.rules || [],
      search: plan.search,
    });
  }, []);

  const handleDelete = (plan: ComparisonPlan) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t("comparison.action.delete"),
      children: t("comparison.confirm.delete", { name: plan.name }),
      onOk: async () => {
        await BApi.comparison.deleteComparisonPlan(plan.id!);
        loadAllPlans();
      },
    });
  };

  const handleDuplicate = async (plan: ComparisonPlan) => {
    await BApi.comparison.duplicateComparisonPlan(plan.id!);
    loadAllPlans();
    toast.success(t("comparison.success.duplicated"));
  };

  const handleExecute = async (plan: ComparisonPlan) => {
    try {
      const r = await BApi.comparison.executeComparisonPlan(plan.id!);
      if (r.code === 0) {
        toast.success(t("comparison.success.taskStarted"));
        // Task status will be updated automatically via SignalR -> BTask store
      } else {
        toast.error(r.message || t("comparison.error.taskFailed"));
      }
    } catch (e: any) {
      toast.error(e.message || t("comparison.error.taskFailed"));
    }
  };

  const handleClearResults = async (plan: ComparisonPlan) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t("comparison.action.clearResults"),
      children: t("comparison.confirm.clearResults", { name: plan.name }),
      onOk: async () => {
        await BApi.comparison.clearComparisonResults(plan.id!);
        loadAllPlans();
        toast.success(t("comparison.success.resultsCleared"));
      },
    });
  };

  const handleViewResults = (plan: ComparisonPlan) => {
    createPortal(Modal, {
      defaultVisible: true,
      size: "7xl",
      title: plan.name,
      footer: false,
      children: (
        <ComparisonResults plan={plan} />
      ),
    });
  };

  const renderEmptyState = () => (
    <div className="flex flex-col items-center justify-center min-h-[400px] gap-4">
      <AiOutlineInbox className="text-6xl text-default-300" />
      <div className="text-default-500 text-center">
        <p className="text-lg mb-2">{t("comparison.empty.noPlans")}</p>
        <p className="text-sm text-default-400">
          {t("comparison.empty.description")}
        </p>
      </div>
      <Button color="primary" startContent={<PlusOutlined />} onPress={handleAdd}>
        {t("comparison.action.createPlan")}
      </Button>
    </div>
  );

  const renderAccordionTitle = (plan: ComparisonPlan) => {
    const task = getTaskForPlan(plan.id!);
    const isRunning = task?.status === BTaskStatus.Running;

    return (
      <div className="flex items-center justify-between w-full">
        {/* Left section: Name & Stats */}
        <div className="flex items-center gap-2">
          <span className="font-medium">{plan.name}</span>
          <Chip size="sm" variant="flat">
            {t("comparison.label.ruleCount", { count: plan.rules?.length || 0 })}
          </Chip>
          {isRunning && (
            <Progress
              className="w-24"
              color="primary"
              size="sm"
              value={task?.percentage || 0}
            />
          )}
          <Chip className="text-default-400" size="sm" variant="light">
            {t("comparison.label.createdAt")}: {plan.createdAt}
          </Chip>
          <Tooltip content={t("comparison.action.edit")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => handleEditBasicInfo(plan)}
            >
              <EditOutlined className="text-base" />
            </Button>
          </Tooltip>
        </div>

      {/* Right section: Actions */}
      <div className="flex items-center gap-1">
        {plan.lastRunAt && (
          <Chip className="text-default-400" size="sm" variant="light">
            {t("comparison.label.lastRun")}: {plan.lastRunAt}
          </Chip>
        )}
        <Tooltip content={t("comparison.action.duplicate")}>
          <Button
            isIconOnly
            size="sm"
            variant="light"
            onPress={() => handleDuplicate(plan)}
          >
            <CopyOutlined className="text-base" />
          </Button>
        </Tooltip>
        {plan.resultGroupCount != null && plan.resultGroupCount > 0 && (
          <Tooltip content={t("comparison.action.clearResults")}>
            <Button
              isIconOnly
              color="warning"
              size="sm"
              variant="light"
              onPress={() => handleClearResults(plan)}
            >
              <ClearOutlined className="text-base" />
            </Button>
          </Tooltip>
        )}
        <Tooltip content={t("comparison.action.delete")}>
          <Button
            isIconOnly
            color="danger"
            size="sm"
            variant="light"
            onPress={() => handleDelete(plan)}
          >
            <DeleteOutlined className="text-base" />
          </Button>
        </Tooltip>
      </div>
    </div>
    );
  };

  return (
    <div className="flex flex-col gap-2">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Button color="primary" size="sm" startContent={<PlusOutlined />} onPress={handleAdd}>
            {t("comparison.action.createPlan")}
          </Button>
          <BetaChip />
          {plans && plans.length > 0 && (
            <span className="text-sm text-default-400">
              {t("comparison.label.planCount")}: {plans.length}
            </span>
          )}
        </div>
      </div>

      {/* Content */}
      {plans ? (
        plans.length === 0 ? (
          renderEmptyState()
        ) : (
          <Accordion
            className="p-0"
            selectedKeys={expandedKeys}
            selectionMode="multiple"
            variant="splitted"
            onSelectionChange={(keys) => {
              if (!keys) {
                setExpandedKeys([]);
              }
              setExpandedKeys(Array.from(keys).map((x) => x as string));
            }}
          >
            {plans.map((plan) => {
              const task = getTaskForPlan(plan.id!);
              const isRunning = task?.status === BTaskStatus.Running;

              return (
                <AccordionItem key={plan.id!.toString()} title={renderAccordionTitle(plan)}>
                  <div className="p-0 flex flex-col gap-4">
                    {/* Filters section */}
                    <Card className="bg-default-50">
                      <CardHeader className="pb-2 flex items-center gap-4">
                        <span className="text-sm font-medium">{t("comparison.label.filters")}</span>
                        <span className="text-xs text-default-400">{t("comparison.description.filters")}</span>
                      </CardHeader>
                      <CardBody className="pt-0">
                        <ResourceFilterController
                          filterDisplayMode={FilterDisplayMode.Simple}
                          autoCreateMediaLibraryFilter={true}
                          group={plan.search?.group}
                          onGroupChange={(group) => {
                            const updatedSearch = { ...plan.search, group };
                            handleFilterChange(plan, updatedSearch);
                          }}
                          tags={plan.search?.tags}
                          onTagsChange={(tags) => {
                            const updatedSearch = { ...plan.search, tags };
                            handleFilterChange(plan, updatedSearch);
                          }}
                          filterLayout="vertical"
                          showRecentFilters
                          showTags
                        />
                      </CardBody>
                    </Card>

                    {/* Rules section */}
                    <Card className="bg-default-50">
                      <CardHeader className="pb-2 flex items-center gap-4">
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-medium">{t("comparison.label.rules")}</span>
                          <span className="text-xs text-default-400">{t("comparison.description.rules")}</span>
                        </div>
                        <Button
                          color="primary"
                          size="sm"
                          startContent={<PlusOutlined />}
                          variant="flat"
                          onPress={() => handleAddRule(plan)}
                        >
                          {t("comparison.action.addRule")}
                        </Button>
                      </CardHeader>
                      <CardBody className="pt-0">
                        {(() => {
                          const rulesWithProps = planRulesWithProperties.get(plan.id!);
                          // Load rules if not loaded
                          if (rulesWithProps === undefined && (plan.rules?.length ?? 0) > 0) {
                            loadRulesWithProperties(plan);
                            return (
                              <div className="text-sm text-default-400 py-2">
                                {t("common.state.loading")}
                              </div>
                            );
                          }
                          if (!rulesWithProps || rulesWithProps.length === 0) {
                            return (
                              <div className="text-sm text-default-400 py-2">
                                {t("comparison.empty.noRules")}
                              </div>
                            );
                          }
                          return (
                            <div className="flex flex-wrap gap-2">
                              {rulesWithProps.map((rule, index) => (
                                <RuleDemonstrator
                                  key={index}
                                  property={rule.property}
                                  propertyId={rule.propertyId}
                                  mode={rule.mode!}
                                  parameter={rule.parameter}
                                  normalize={rule.normalize}
                                  weight={rule.weight!}
                                  isVeto={rule.isVeto!}
                                  vetoThreshold={rule.vetoThreshold}
                                  oneNullBehavior={rule.oneNullBehavior!}
                                  bothNullBehavior={rule.bothNullBehavior!}
                                  onClick={() => handleEditRule(plan, rule, index)}
                                  onDelete={() => handleDeleteRule(plan, index)}
                                />
                              ))}
                            </div>
                          );
                        })()}
                      </CardBody>
                    </Card>

                    {/* Execution section */}
                    <Card className="bg-default-50">
                      <CardHeader className="pb-2 flex items-center gap-4">
                        <span className="text-sm font-medium">{t("comparison.action.execute")}</span>
                        <span className="text-xs text-default-400">{t("comparison.description.execute")}</span>
                      </CardHeader>
                      <CardBody className="pt-0 flex flex-col gap-3">
                        <div className="flex items-center gap-4">
                          <div className="flex items-center gap-2">
                            <span className="text-sm text-default-500">{t("comparison.label.threshold")}:</span>
                            <EditableValue<number, { value?: number; onValueChange?: (v: number) => void }, { value?: number; isReadOnly?: boolean; onClick?: () => void }>
                              value={plan.threshold ?? 80}
                              Viewer={({ value, onClick }) => (
                                <Chip
                                  className="cursor-pointer hover:bg-default-200"
                                  size="sm"
                                  variant="flat"
                                  onClick={onClick}
                                >
                                  {value}%
                                </Chip>
                              )}
                              Editor={({ value, onValueChange }) => (
                                <NumberInput
                                  autoFocus
                                  className="w-24"
                                  endContent={<span className="text-default-400">%</span>}
                                  max={100}
                                  min={0}
                                  size="sm"
                                  step={1}
                                  value={value}
                                  onValueChange={(v) => onValueChange?.(v ?? 80)}
                                />
                              )}
                              onSubmit={(v) => handleThresholdChange(plan, v ?? 80)}
                              trigger="viewer"
                            />
                          </div>
                        </div>
                        <ScoringExplanation mode="inline" className="p-3 bg-default-100 rounded-lg" />
                        <div className="flex items-center gap-3">
                          <Tooltip
                            content={!plan.rules?.length ? t("comparison.description.executeDisabledNoRules") : undefined}
                            isDisabled={!!plan.rules?.length}
                          >
                            <span>
                              <Button
                                color="success"
                                isDisabled={isRunning || !plan.rules?.length}
                                size="sm"
                                startContent={<PlayCircleOutlined />}
                                onPress={() => handleExecute(plan)}
                              >
                                {plan.lastRunAt ? t("comparison.action.reExecute") : t("comparison.action.execute")}
                              </Button>
                            </span>
                          </Tooltip>
                          {(plan.resultGroupCount ?? 0) > 0 && (
                            <Button
                              color="warning"
                              size="sm"
                              variant="flat"
                              onPress={() => handleViewResults(plan)}
                            >
                              {t("comparison.action.viewGroups", { count: plan.resultGroupCount })}
                            </Button>
                          )}
                          {(plan.resultGroupCount ?? 0) === 0 && plan.lastRunAt != null && (
                            <Chip color="success" size="sm" variant="flat">
                              {t("comparison.label.noResultsFound")}
                            </Chip>
                          )}
                        </div>
                      </CardBody>
                    </Card>
                  </div>
                </AccordionItem>
              );
            })}
          </Accordion>
        )
      ) : (
        <div className="flex items-center justify-center min-h-[400px]">
          <Spinner size="lg" />
        </div>
      )}

      {/* Rule Modal */}
      <RuleModal
        isOpen={ruleModalOpen}
        rule={editingRule}
        onClose={() => setRuleModalOpen(false)}
        onSave={handleSaveRule}
      />
    </div>
  );
};

ComparisonPage.displayName = "ComparisonPage";

export default ComparisonPage;
