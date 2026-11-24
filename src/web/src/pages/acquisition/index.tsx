import WorkflowIntegrationHint from "@/components/Workflow/WorkflowIntegrationHint";
("use client");

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Drawer, DrawerBody, DrawerContent, DrawerHeader } from "@heroui/react";
import {
  AiOutlineBranches,
  AiOutlineDownload,
  AiOutlineFolderOpen,
  AiOutlinePlusCircle,
  AiOutlineSetting,
  AiOutlineDelete,
  AiOutlineLink,
} from "react-icons/ai";

import InboxDrawer from "./components/InboxDrawer";
import AcquisitionRow from "./components/AcquisitionRow";
import StartAcquisitionModal from "./components/StartAcquisitionModal";
import AcquisitionSettingsModal from "./components/AcquisitionSettingsModal";
import AcquisitionThroughput from "./components/throughput";
import AcquisitionReserveSetting from "./components/AcquisitionReserveSetting";
import AcquisitionAttentionFilters from "./components/AcquisitionAttentionFilters";
import AcquisitionTaskSearch from "./components/AcquisitionTaskSearch";
import DeletedAcquisitionTasksModal from "./components/DeletedAcquisitionTasksModal";
import DeleteAcquisitionTasksModal from "./components/DeleteAcquisitionTasksModal";
import AcquisitionContinueModal from "./components/AcquisitionContinueModal";
import AcquisitionSelectionButtons from "./components/AcquisitionSelectionButtons";
import { acquisitionActionLimits, acquisitionSelectionLimit } from "./acquisitionSelection";
import { normalizeAcquisitionFilters, type AcquisitionFilterSelection } from "./acquisitionFilters";
import {
  readAcquisitionListPreferences,
  writeAcquisitionListPreferences,
  type AcquisitionListPreferences,
} from "./listPreferences";
import AcquisitionDownloadDestinationModal from "./components/AcquisitionDownloadDestinationModal";
import AcquisitionStorageMaintenanceModal from "./components/AcquisitionStorageMaintenanceModal";
import useAcquisitionTasks from "./useAcquisitionTasks";
import RecipeCatalog from "./components/RecipeCatalog";
import AcquisitionStageFilters from "./components/AcquisitionStageFilters";
import AcquisitionDownloadSourceFilter from "./components/AcquisitionDownloadSourceFilter";
import AcquisitionRuntimeCapabilityFilters from "./components/AcquisitionRuntimeCapabilityFilters";
import { downloadSourceLabel } from "@/components/DownloadSource";
import SourceAvailabilityReviewModal from "@/components/SourceAvailability/SourceAvailabilityReviewModal";

import BApi from "@/sdk/BApi";
import { HelpCenterButton } from "@/components/HelpCenter";
import { Button, Badge, Checkbox, Pagination, Select, Spinner, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PlanTaskRow from "@/components/Acquire/PlanTask/PlanTaskRow";
import PlanTaskDetailModal from "@/components/Acquire/PlanTask/PlanTaskDetailModal";
import type { PlanTask } from "@/components/Acquire/PlanTask/models";
import useRuntimeOverviews from "@/components/Acquire/PlanTask/useRuntimeOverviews";
import LegacyTaskDetailModal from "./components/LegacyTaskDetailModal";
import { acquisitionStages, type AcquisitionStageFilter } from "@/components/Acquire/taskStage";

export type AcquisitionTaskVm =
  components["schemas"]["Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionTask"];
export type AcquisitionRecipeVm =
  components["schemas"]["Bakabase.Modules.Acquisition.Abstractions.Services.AcquisitionRecipeSummary"];

const AcquisitionPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { createPortal } = useBakabaseContext();

  const [recipes, setRecipes] = useState<AcquisitionRecipeVm[]>([]);
  const [initialPreferences] = useState(() => {
    const saved = readAcquisitionListPreferences();
    const requested = searchParams.get("stage");
    return ["all", ...acquisitionStages].includes(requested ?? "")
      ? { ...saved, stage: requested as AcquisitionStageFilter, attentionTags: [], facts: [] }
      : saved;
  });
  const [stage, setStage] = useState<AcquisitionStageFilter>(initialPreferences.stage);
  const [downloadSource, setDownloadSource] = useState(initialPreferences.downloadSource);
  const [capabilityId, setCapabilityId] = useState(initialPreferences.capabilityId);
  const [executionState, setExecutionState] = useState(initialPreferences.executionState);
  const [keyword, setKeyword] = useState(initialPreferences.keyword);
  const [searchKeyword, setSearchKeyword] = useState(initialPreferences.keyword.trim());
  const [filters, setFilters] = useState<AcquisitionFilterSelection>({
    attentionTags: initialPreferences.attentionTags,
    facts: initialPreferences.facts,
  });
  const filtersKey = JSON.stringify(filters);
  const [page, setPage] = useState(1);
  const [sort, setSort] = useState<AcquisitionListPreferences["sort"]>(initialPreferences.sort);
  const [selectedTaskIds, setSelectedTaskIds] = useState(new Set<number>());
  const openedTask = useRef<string>();
  const [settingsRevision, setSettingsRevision] = useState(0);
  const [recipesOpen, setRecipesOpen] = useState(false);
  const filterPending = keyword.trim() !== searchKeyword;
  const {
    data,
    stageCounts,
    downloadSourceCounts,
    attentionCounts,
    factCounts,
    runtimeCapabilityCounts,
    runtimeExecutionStateCounts,
    runtimeCapabilities,
    taskRuntimeFacets,
    queueTaskCount,
    loading,
    failed: loadFailed,
    refresh: load,
  } = useAcquisitionTasks({
    keyword: searchKeyword,
    ...(downloadSource ? { downloadSource } : {}),
    ...(capabilityId ? { capabilityId } : {}),
    ...(executionState ? { executionState } : {}),
    stage,
    ...filters,
    page,
    pageSize: 24,
    sort,
  }, filterPending);
  const tasks = (data?.items ?? []) as PlanTask[];
  const checkableIds = data?.checkableTaskIds ?? [];
  const filteredIds = data?.filteredTaskIds ?? [];
  // A same-query poll preserves the accepted rows. Toggling HeroUI's disabled
  // appearance every five seconds makes checked boxes flash and interrupts clicks.
  // Local selection can remain editable; execution gets its own fixed-ID preview.
  // A new scope has no data, and a failed refresh remains protected until reloaded.
  const selectionDisabled = !data || loadFailed || filterPending;
  const runtimeCapabilityLabel = capabilityId
    ? Object.values(taskRuntimeFacets ?? {}).find((facet) => facet.capabilityId === capabilityId)?.capabilityName || capabilityId
    : undefined;
  const scopeLabel = [
    t(`acquisition.stage.${stage}`),
    downloadSource && downloadSourceLabel(downloadSource, t),
    runtimeCapabilityLabel,
    executionState && t(`acquisition.runtime.executionState.${executionState}`, { defaultValue: executionState }),
    ...filters.attentionTags.map((tag) => t(`acquisition.attention.tag.${tag}`)),
    ...filters.facts.map((fact) => t(`acquisition.filters.fact.${fact}`)),
    filters.attentionTags.length + filters.facts.length > 1 && t("acquisition.filters.matchAll"),
    searchKeyword,
  ]
    .filter(Boolean)
    .join(" · ");

  useEffect(() => {
    if (keyword.trim() === searchKeyword) return;
    const timer = setTimeout(() => {
      setSearchKeyword(keyword.trim());
      setPage(1);
    }, 250);
    return () => clearTimeout(timer);
  }, [keyword, searchKeyword]);
  useEffect(() => {
    writeAcquisitionListPreferences({
      stage,
      keyword: searchKeyword,
      downloadSource,
      capabilityId,
      executionState,
      sort,
      ...filters,
    });
  }, [stage, searchKeyword, downloadSource, capabilityId, executionState, sort, filtersKey]);
  // Pagination and sorting preserve the explicit selection; changing its scope does not.
  useEffect(() => {
    setSelectedTaskIds(new Set());
  }, [stage, downloadSource, capabilityId, executionState, filtersKey, searchKeyword]);
  useEffect(() => {
    if (!data || loading || loadFailed || data.taskIdsTruncated) return;
    const current = new Set(data.filteredTaskIds ?? []);
    setSelectedTaskIds((previous) => {
      const next = new Set([...previous].filter((id) => current.has(id)));
      return next.size === previous.size ? previous : next;
    });
  }, [data, loading, loadFailed]);
  useEffect(() => {
    const requested = searchParams.get("stage");
    if (["all", ...acquisitionStages].includes(requested ?? "")) {
      setStage(requested as AcquisitionStageFilter);
      setCapabilityId(undefined);
      setExecutionState(undefined);
      setFilters({ attentionTags: [], facts: [] });
      setPage(1);
    }
  }, [searchParams]);
  useEffect(() => {
    let current = true;
    void BApi.acquisition
      .getAcquisitionRecipes()
      .then((result) => {
        if (current && !result.code) setRecipes(result.data ?? []);
      })
      .catch(() => {});
    return () => {
      current = false;
    };
  }, []);

  const openTask = useCallback(
    (task: PlanTask) => {
      if (task.builderId) createPortal(PlanTaskDetailModal, { taskId: task.id, onChanged: load });
      else
        createPortal(LegacyTaskDetailModal, {
          taskId: task.id,
          recipes,
          onChanged: load,
          onEditRecipe: (id: number) => navigate(`/workflows/editor?id=${id}`),
        });
    },
    [createPortal, load, navigate, recipes],
  );
  const openTaskById = useCallback(
    (id: number) => {
      void BApi.acquisition
        .getAcquisition(id, { sensitiveResponse: true, showErrorToast: false })
        .then((result) => {
          if (!result.code && result.data) openTask(result.data as PlanTask);
        })
        .catch(() => {});
    },
    [openTask],
  );

  useEffect(() => {
    const taskId = searchParams.get("taskId");
    if (!taskId || !/^\d+$/.test(taskId) || Number(taskId) <= 0 || openedTask.current === taskId)
      return;
    // A deep link may refer to a task outside this page, or to retained cancelled history.
    let current = true;
    void BApi.acquisition
      .getAcquisition(Number(taskId), { sensitiveResponse: true })
      .then((result) => {
        if (current && !result.code && result.data) {
          openedTask.current = taskId;
          openTask(result.data as PlanTask);
        }
      })
      .catch(() => {});
    return () => {
      current = false;
    };
  }, [searchParams, openTask]);

  const pageCount = Math.max(1, Math.ceil((data?.totalCount ?? 0) / 24));
  const currentPage = data?.page ?? page;
  const visibleTasks = tasks;
  const visibleTaskIds = visibleTasks.map((task) => task.id);
  const selectedOnPage = visibleTaskIds.filter((id) => selectedTaskIds.has(id)).length;
  const pageSelectionExceedsLimit =
    selectedTaskIds.size + visibleTaskIds.length - selectedOnPage > acquisitionSelectionLimit;
  const changePageSelection = (checked: boolean) => {
    if (selectionDisabled || (checked && pageSelectionExceedsLimit)) return;
    setSelectedTaskIds((previous) => {
      const next = new Set(previous);
      for (const id of visibleTaskIds) {
        if (checked) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  };
  const selectedIds = [...selectedTaskIds];
  const checkableSet = new Set(checkableIds);
  const selectedSourceIds = selectedIds.filter((id) => checkableSet.has(id));
  const hasScopedFilter =
    !!downloadSource ||
    !!capabilityId ||
    !!executionState ||
    stage !== "all" ||
    filters.attentionTags.length > 0 ||
    filters.facts.length > 0 ||
    searchKeyword.trim().length > 0;
  const scopeLoading = loading && !data;
  // Both continuation methods use the same captured selection or complete narrowed filter.
  const filteredAnalysis = selectedIds.length === 0 && hasScopedFilter;
  const continuationIds = selectedIds.length
    ? selectedIds
    : filteredAnalysis && !data?.taskIdsTruncated
      ? filteredIds
      : [];
  const selectedScopeLabel = (ids: number[]) =>
    `${t("acquisition.actionbar.selectedScope", { count: ids.length })} · ${scopeLabel}`;
  const taskActions: {
    key: keyof typeof acquisitionActionLimits;
    label: string;
    ids: number[];
    limit: number;
    color?: "primary" | "danger";
    icon?: React.ReactNode;
    open: (ids: number[]) => void;
  }[] = [
    {
      key: "continue",
      label: "acquisition.continueEntry.open",
      ids: continuationIds,
      limit: acquisitionActionLimits.continue,
      color: "primary",
      open: (ids) =>
        createPortal(AcquisitionContinueModal, {
          taskIds: ids,
          scopeLabel: filteredAnalysis
            ? `${t("acquisition.actionbar.filteredScope", { count: ids.length })} · ${scopeLabel}`
            : selectedScopeLabel(ids),
          filteredAnalysis,
          onChanged: load,
          onOpenTask: openTaskById,
        }),
    },
    {
      key: "checkSources",
      label: "acquisition.actionbar.checkSources",
      ids: selectedSourceIds,
      limit: acquisitionActionLimits.checkSources,
      icon: <AiOutlineLink aria-hidden />,
      open: (ids) =>
        createPortal(SourceAvailabilityReviewModal, {
          taskIds: ids,
          scopeLabel: selectedScopeLabel(ids),
          onChecked: load,
          onChanged: load,
        }),
    },
    {
      key: "downloadDestination",
      label: "acquisition.actionbar.downloadDestination",
      ids: selectedIds,
      limit: acquisitionActionLimits.downloadDestination,
      icon: <AiOutlineFolderOpen aria-hidden />,
      open: (ids) =>
        createPortal(AcquisitionDownloadDestinationModal, {
          taskIds: ids,
          scopeLabel: selectedScopeLabel(ids),
          onChanged: load,
        }),
    },
    {
      key: "storagePolicy",
      label: "acquisition.actionbar.storagePolicy",
      ids: selectedIds,
      limit: acquisitionActionLimits.storagePolicy,
      icon: <AiOutlineSetting aria-hidden />,
      open: (ids) =>
        createPortal(AcquisitionStorageMaintenanceModal, {
          mode: "policy",
          taskIds: ids,
          scopeLabel: selectedScopeLabel(ids),
          onChanged: load,
        }),
    },
    {
      key: "workspaceCleanup",
      label: "acquisition.actionbar.workspaceCleanup",
      ids: selectedIds,
      limit: acquisitionActionLimits.workspaceCleanup,
      icon: <AiOutlineFolderOpen aria-hidden />,
      open: (ids) =>
        createPortal(AcquisitionStorageMaintenanceModal, {
          mode: "cleanup",
          taskIds: ids,
          scopeLabel: selectedScopeLabel(ids),
          onChanged: load,
        }),
    },
    {
      key: "delete",
      label: "acquisition.actionbar.delete",
      ids: selectedIds,
      limit: acquisitionActionLimits.delete,
      color: "danger",
      icon: <AiOutlineDelete aria-hidden />,
      open: (ids) =>
        createPortal(DeleteAcquisitionTasksModal, {
          taskIds: ids,
          taskNames: Object.fromEntries(
            tasks.map((task) => [task.id, task.title?.trim() || task.resourceName?.trim() || ""]),
          ),
          scopeLabel: selectedScopeLabel(ids),
          onChanged: load,
        }),
    },
  ];
  const {
    overviews,
    failed: runtimeFailed,
    loading: runtimeLoading,
  } = useRuntimeOverviews(visibleTasks);

  return (
    <div className="@container/acquisition flex min-w-0 flex-col gap-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-xl font-semibold">{t<string>("menu.acquisition")}</h1>
          <HelpCenterButton topic="acquisition" />
          <WorkflowIntegrationHint surface="acquisition" />
        </div>
        <div className="flex min-w-0 max-w-full flex-wrap items-center gap-2 lg:ml-auto">
          <AcquisitionThroughput />
          <AcquisitionReserveSetting revision={settingsRevision} />
          <Tooltip
            content={t("acquisition.continueEntry.queueCountHint", { count: queueTaskCount })}
          >
            <Badge
              content={queueTaskCount}
              color="primary"
              size="sm"
              isInvisible={!queueTaskCount}
              showOutline={false}
            >
              <Button
                size="sm"
                variant="flat"
                onPress={() => createPortal(AcquisitionContinueModal, { onChanged: load })}
              >
                {t("acquisition.continueEntry.queue")}
              </Button>
            </Badge>
          </Tooltip>
          <Button
            size="sm"
            startContent={<AiOutlineBranches aria-hidden className="text-base" />}
            variant="flat"
            onPress={() => setRecipesOpen(true)}
          >
            {t<string>("acquisition.recipes.title")}
          </Button>
          <Tooltip content={t<string>("acquisition.recipes.createDescription")}>
            <Button
              size="sm"
              startContent={<AiOutlinePlusCircle aria-hidden className="text-base" />}
              variant="flat"
              onPress={() => navigate("/workflows/editor?template=acquisition")}
            >
              {t<string>("acquisition.recipes.create")}
            </Button>
          </Tooltip>
          <div className="flex items-center gap-2">
            <Tooltip content={t<string>("acquisition.inbox.hint")}>
              <Button
                size="sm"
                startContent={<AiOutlineFolderOpen aria-hidden className="text-base" />}
                variant="flat"
                onPress={() => createPortal(InboxDrawer, { onClaimed: load })}
              >
                {t<string>("acquisition.inbox.open")}
              </Button>
            </Tooltip>
            <Button size="sm" variant="light" startContent={<AiOutlineDelete aria-hidden className="text-base" />}
              onPress={() => createPortal(DeletedAcquisitionTasksModal, { onOpenTask: openTaskById })}>
              {t<string>("acquisition.delete.history.title")}
            </Button>
            <Tooltip content={t<string>("acquisition.settings.description")}>
              <Button
                size="sm"
                startContent={<AiOutlineSetting aria-hidden className="text-base" />}
                variant="light"
                onPress={() =>
                  createPortal(AcquisitionSettingsModal, {
                    onDone: () => {
                      setSettingsRevision((n) => n + 1);
                      void load();
                    },
                  })
                }
              >
                {t<string>("acquisition.settings.title")}
              </Button>
            </Tooltip>
          </div>
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <AcquisitionTaskSearch
          value={keyword}
          onValueChange={(value) => {
            setSelectedTaskIds(new Set());
            setKeyword(value);
            setPage(1);
          }}
          onSearch={() => {
            setSearchKeyword(keyword.trim());
            setPage(1);
          }}
          onOpenTask={openTask}
        />
        <AcquisitionStageFilters
          counts={stageCounts}
          value={stage}
          onChange={(value) => {
            setStage(value);
            if (value !== "all") {
              setCapabilityId(undefined);
              setExecutionState(undefined);
              setFilters({ attentionTags: [], facts: [] });
            }
            setPage(1);
          }}
        />
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Select
            aria-label={t("acquisition.sort.label")}
            className="w-48"
            size="sm"
            disallowEmptySelection
            selectedKeys={[sort]}
            dataSource={["newest", "lastProcessed", "sizeAsc", "sizeDesc"].map((value) => ({
              value,
              label: t(`acquisition.sort.${value}`),
            }))}
            onSelectionChange={(keys) => {
              const value = Array.from(keys)[0];
              if (
                value === "newest" ||
                value === "lastProcessed" ||
                value === "sizeAsc" ||
                value === "sizeDesc"
              ) {
                setSort(value);
                setPage(1);
              }
            }}
          />
          <Button
            color="primary"
            size="sm"
            startContent={<AiOutlineDownload aria-hidden className="text-base" />}
            onPress={() =>
              createPortal(StartAcquisitionModal, {
                onStarted: (taskId: number) => {
                  void load();
                  setStage("all");
                  setDownloadSource(undefined);
                  setCapabilityId(undefined);
                  setExecutionState(undefined);
                  setFilters({ attentionTags: [], facts: [] });
                  setKeyword("");
                  setPage(1);
                  createPortal(PlanTaskDetailModal, { taskId, onChanged: load });
                },
              })
            }
          >
            {t<string>("acquisition.start")}
          </Button>
        </div>
      </div>
      <AcquisitionDownloadSourceFilter
        counts={downloadSourceCounts}
        value={downloadSource}
        onChange={(value) => {
          setDownloadSource(value);
          setPage(1);
          setSelectedTaskIds(new Set());
        }}
      />
      <AcquisitionRuntimeCapabilityFilters
        capabilityCounts={runtimeCapabilityCounts}
        executionStateCounts={runtimeExecutionStateCounts}
        capabilities={runtimeCapabilities}
        facets={taskRuntimeFacets}
        capabilityId={capabilityId}
        executionState={executionState}
        disabled={loadFailed || filterPending}
        onCapabilityChange={(value) => {
          setCapabilityId(value);
          setPage(1);
          setSelectedTaskIds(new Set());
        }}
        onExecutionStateChange={(value) => {
          setExecutionState(value);
          setPage(1);
          setSelectedTaskIds(new Set());
        }}
      />
      <AcquisitionAttentionFilters
        counts={attentionCounts}
        factCounts={factCounts}
        matchingCount={filterPending || loadFailed ? undefined : data?.totalCount}
        value={filters}
        disabled={loadFailed || filterPending}
        onChange={(value) => {
          setSelectedTaskIds(new Set());
          setFilters(normalizeAcquisitionFilters(value));
          setPage(1);
        }}
      />

      {loadFailed && (
        <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger">
          <span>{t("acquisition.plan.loadFailed")}</span>
          <Button size="sm" variant="light" onPress={() => void load()}>
            {t("common.action.retry")}
          </Button>
        </div>
      )}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 text-xs">
        <div className="flex min-w-0 max-w-full flex-wrap items-center gap-2">
          <Checkbox
            size="sm"
            isSelected={!!visibleTaskIds.length && selectedOnPage === visibleTaskIds.length}
            isIndeterminate={selectedOnPage > 0 && selectedOnPage < visibleTaskIds.length}
            isDisabled={
              selectionDisabled ||
              !visibleTaskIds.length ||
              (selectedOnPage < visibleTaskIds.length && pageSelectionExceedsLimit)
            }
            onValueChange={changePageSelection}
          >
            {t("acquisition.selection.page", { count: visibleTaskIds.length })}
          </Checkbox>
          <AcquisitionSelectionButtons
            pageIds={visibleTaskIds}
            filteredIds={filteredIds}
            totalCount={data?.totalCount ?? 0}
            truncated={data?.taskIdsTruncated === true}
            disabled={selectionDisabled}
            otherPagesSelected={selectedTaskIds.size > selectedOnPage}
            showCrossPageSelection={
              pageCount > 1 && selectedOnPage === visibleTaskIds.length && visibleTaskIds.length > 0
            }
            onSelect={(ids) => setSelectedTaskIds(new Set(ids))}
          />
          <Tooltip content={t("acquisition.selection.hint")} delay={1000} placement="bottom">
            <span className="text-default-500" aria-live="polite">
              {t("acquisition.selection.summary", {
                total: data?.totalCount ?? "—",
                count: selectedTaskIds.size,
              })}
            </span>
          </Tooltip>
          {!!selectedTaskIds.size && (
            <Button size="sm" variant="light" onPress={() => setSelectedTaskIds(new Set())}>
              {t("acquisition.selection.clear")}
            </Button>
          )}
        </div>
        <div className="ml-auto flex flex-wrap items-center gap-1">
          {taskActions.map((action) => {
            const overLimit = action.ids.length > action.limit;
            const disabled = selectionDisabled || scopeLoading || !action.ids.length || overLimit;
            const hint =
              action.key === "continue" && filteredAnalysis && data?.taskIdsTruncated
                ? t("acquisition.continueActions.narrowFilter")
                : overLimit
                  ? t("acquisition.actionbar.limit", {
                      count: action.ids.length,
                      limit: action.limit,
                    })
                  : action.key === "continue" && filteredAnalysis && action.ids.length
                    ? t("acquisition.actionbar.filteredScope", { count: action.ids.length })
                    : !selectedTaskIds.size
                      ? t("acquisition.actionbar.selectFirst")
                      : action.key === "checkSources"
                        ? t("acquisition.actionbar.checkable", {
                            count: action.ids.length,
                            total: selectedTaskIds.size,
                          })
                        : selectedTaskIds.size
                          ? t("acquisition.actionbar.selectedScope", { count: action.ids.length })
                          : t("acquisition.actionbar.filteredScope", { count: action.ids.length });
            return (
              <Tooltip key={action.key} content={hint} delay={1000} placement="bottom">
                <span>
                  <Button
                    size="sm"
                    variant="flat"
                    color={action.color}
                    startContent={action.icon}
                    isDisabled={disabled}
                    onPress={() => {
                      if (!disabled) action.open([...action.ids]);
                    }}
                  >
                    {t(action.label)}
                  </Button>
                </span>
              </Tooltip>
            );
          })}
        </div>
        {data?.taskIdsTruncated && (
          <span className="text-warning-600">{t("acquisition.selection.truncated")}</span>
        )}
        {runtimeFailed && (
          <span role="status" className="text-warning-600">
            {t("acquisition.runtime.refreshFailed")}
          </span>
        )}
        {runtimeLoading && !visibleTasks.some((task) => overviews[task.id]) && (
          <span className="text-default-500">{t("acquisition.runtime.loading")}</span>
        )}
      </div>
      {loading && !data ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
        </div>
      ) : tasks.length === 0 && !loadFailed ? (
        <div className="text-center text-default-500 py-10">
          {t<string>("acquisition.tasks.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {visibleTasks.map((task) => (
            <div key={task.id} className="flex items-start gap-2">
              <Checkbox
                className="mt-3 shrink-0"
                size="sm"
                aria-label={t("acquisition.selection.task", { id: task.id })}
                isSelected={selectedTaskIds.has(task.id)}
                isDisabled={
                  selectionDisabled ||
                  (!selectedTaskIds.has(task.id) &&
                    selectedTaskIds.size >= acquisitionSelectionLimit)
                }
                onValueChange={(checked) => {
                  if (
                    selectionDisabled ||
                    (checked && selectedTaskIds.size >= acquisitionSelectionLimit)
                  )
                    return;
                  setSelectedTaskIds((previous) => {
                    const next = new Set(previous);
                    if (checked) next.add(task.id);
                    else next.delete(task.id);
                    return next;
                  });
                }}
              />
              <div className="min-w-0 flex-1">
                {task.builderId ? (
                  <PlanTaskRow
                    key={task.id}
                    task={task}
                    overview={overviews[task.id]}
                    sizeEstimate={data?.sizeEstimates?.[task.id]}
                    facts={data?.taskFacts?.[task.id]}
                    onOpen={() => openTask(task)}
                    onDelete={() =>
                      createPortal(DeleteAcquisitionTasksModal, {
                        taskIds: [task.id],
                        taskNames: {
                          [task.id]: task.title?.trim() || task.resourceName?.trim() || "",
                        },
                        onChanged: load,
                      })
                    }
                  />
                ) : (
                  <AcquisitionRow
                    key={task.id}
                    recipes={recipes}
                    task={task as AcquisitionTaskVm}
                    overview={overviews[task.id]}
                    sizeEstimate={data?.sizeEstimates?.[task.id]}
                    facts={data?.taskFacts?.[task.id]}
                    onChanged={load}
                    onOpen={() => openTask(task)}
                    onDelete={() =>
                      createPortal(DeleteAcquisitionTasksModal, {
                        taskIds: [task.id],
                        taskNames: {
                          [task.id]: task.title?.trim() || task.resourceName?.trim() || "",
                        },
                        onChanged: load,
                      })
                    }
                    onEditRecipe={(id) => navigate(`/workflows/editor?id=${id}`)}
                  />
                )}
              </div>
            </div>
          ))}
        </div>
      )}
      {pageCount > 1 && (
        <Pagination
          aria-label={t("acquisition.tasks.pagination")}
          page={currentPage}
          total={pageCount}
          onChange={setPage}
        />
      )}

      <Drawer isOpen={recipesOpen} placement="right" size="3xl" onOpenChange={setRecipesOpen}>
        <DrawerContent>
          <DrawerHeader>{t<string>("acquisition.recipes.title")}</DrawerHeader>
          <DrawerBody>
            {loading ? (
              <div className="flex justify-center py-10">
                <Spinner aria-label={t<string>("acquisition.recipes.loading")} size="lg" />
              </div>
            ) : (
              <RecipeCatalog
                recipes={recipes}
                onOpen={(id) => {
                  setRecipesOpen(false);
                  navigate(`/workflows/editor?id=${id}`);
                }}
              />
            )}
          </DrawerBody>
        </DrawerContent>
      </Drawer>
    </div>
  );
};

export default AcquisitionPage;
