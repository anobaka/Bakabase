"use client";

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  ArrowRightOutlined,
  ReloadOutlined,
  DeleteOutlined,
  EditOutlined,
  HistoryOutlined,
  PlayCircleOutlined,
  PlusCircleOutlined,
  ThunderboltOutlined,
} from "@ant-design/icons";

import { HelpCenterButton } from "@/components/HelpCenter";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import ManualRunModal from "@/components/Workflow/ManualRunModal";
import { getWorkflowTriggerUI } from "@/components/Workflow/Triggers";
import { activityDisplayName, triggerDisplayName } from "@/components/Workflow/displayNames";
import { workflowLabel } from "@/components/Workflow/builtinLabels";
import { workflowDescription } from "@/components/Workflow/metadata";
import WorkflowDiagnostics, {
  hasWorkflowConfigurationErrors,
} from "@/components/Workflow/WorkflowDiagnostics";
import { useSavedWorkflowValidation } from "@/components/Workflow/useWorkflowValidation";
import { useWorkflowTriggerDescriptors } from "@/components/Workflow/triggerPresentation";
import WorkflowTriggerBadge from "@/components/Workflow/WorkflowTriggerBadge";
import TriggerUsageSummary from "@/components/Workflow/TriggerUsageSummary";
import TemplateLibrary from "@/components/Workflow/TemplateLibrary";
import { PresetUsage, workflowPresetGuide } from "@/components/Workflow/presetGuides";
import BApi from "@/sdk/BApi";
import { Button, Chip, Modal, Spinner, Switch, Tab, Tabs, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type WorkflowVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];

function formatTime(iso?: string | null): string | null {
  if (!iso) return null;
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

const WorkflowPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const triggerFilter = params.get("triggerKind")?.trim() || null;
  const { createPortal } = useBakabaseContext();

  const [workflows, setWorkflows] = useState<WorkflowVm[]>([]);
  const {
    data: triggers = [],
    isError: triggersFailed,
    refetch: reloadTriggers,
  } = useWorkflowTriggerDescriptors();
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const loadController = useRef<AbortController>();
  const [selection, setSelection] = useState({
    filter: triggerFilter,
    section: triggerFilter ? "all" : "custom",
  });
  const section =
    selection.filter === triggerFilter ? selection.section : triggerFilter ? "all" : "custom";
  const checks = useSavedWorkflowValidation(workflows);
  // Single drawer instance — opening for a different workflow replaces the
  // selection. Using createPortal would spawn a new component per click.
  const [runsDrawerFor, setRunsDrawerFor] = useState<WorkflowVm | null>(null);

  const load = useCallback(async () => {
    loadController.current?.abort();
    const controller = new AbortController();

    loadController.current = controller;
    setLoading(true);
    setLoadFailed(false);
    try {
      const response = await BApi.workflow.searchWorkflows({}, { signal: controller.signal });

      if (controller.signal.aborted) return;
      if (response.code || !response.data) throw new Error("Workflows unavailable");
      setWorkflows(response.data as WorkflowVm[]);
    } catch {
      if (!controller.signal.aborted) setLoadFailed(true);
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();

    return () => loadController.current?.abort();
  }, [load]);

  const triggerNameByKind = new Map(triggers.map((tr) => [tr.kind, tr.displayName]));
  const triggerByKind = new Map(triggers.map((tr) => [tr.kind, tr]));

  // A manual run bypasses the enabled switch and the trigger filter, so it works on a workflow
  // that is still being built — which is when trying it out matters most.
  const handleRun = async (wf: WorkflowVm) => {
    const trigger = triggerByKind.get(wf.triggerKind);

    if (!trigger) return;
    if (trigger.supportsManualRun === false) {
      const entry = getWorkflowTriggerUI(wf.triggerKind)?.runEntry;

      if (entry) navigate(entry.path);

      return;
    }

    if (hasWorkflowConfigurationErrors(checks.getState(wf.id).result)) {
      navigate(`/workflows/editor?id=${wf.id}`);

      return;
    }

    if (trigger?.requiresManualPayload) {
      createPortal(ManualRunModal, {
        workflowId: wf.id,
        workflowName: workflowLabel(wf, t),
        trigger,
        onRan: () => setRunsDrawerFor(wf),
      });

      return;
    }

    const rsp = await BApi.workflow.runWorkflowManually(wf.id, {});

    if (!rsp.code) {
      toast.success(t<string>("workflow.manualRun.started"));
      setRunsDrawerFor(wf);
    }
  };

  const handleAdd = () => navigate("/workflows/editor");
  const handleAddFromTemplate = () =>
    createPortal(TemplateLibrary, { workflows, onChoose: navigate });
  const handleEdit = (wf: WorkflowVm) => navigate(`/workflows/editor?id=${wf.id}`);
  const handleDelete = (wf: WorkflowVm) =>
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("workflow.delete.confirm"),
      children: workflowLabel(wf, t),
      onOk: async () => {
        await BApi.workflow.deleteWorkflow(wf.id);
        await load();
      },
    });
  const handleToggleEnabled = async (wf: WorkflowVm, next: boolean) => {
    const response = await BApi.workflow.patchWorkflow(wf.id, { enabled: next });

    if (response.code) return;
    setWorkflows((arr) => arr.map((w) => (w.id === wf.id ? { ...w, enabled: next } : w)));
  };
  const filteredWorkflows = triggerFilter
    ? workflows.filter((wf) => wf.triggerKind === triggerFilter)
    : workflows;
  const displayedWorkflows = filteredWorkflows.filter(
    (wf) => section === "all" || (section === "builtin" ? wf.isBuiltin : !wf.isBuiltin),
  );
  const clearTriggerFilter = () => {
    const next = new URLSearchParams(params);

    next.delete("triggerKind");
    setParams(next);
  };

  return (
    <div className="flex flex-col gap-3 p-4">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="text-lg font-semibold">{t<string>("workflow.title")}</h2>
        <HelpCenterButton topic="workflow" />
        <Button color="primary" size="sm" startContent={<PlusCircleOutlined />} onPress={handleAdd}>
          {t<string>("workflow.action.add")}
        </Button>
        <Button
          size="sm"
          startContent={<ThunderboltOutlined />}
          variant="flat"
          onPress={handleAddFromTemplate}
        >
          {t<string>("workflow.templates.title")}
        </Button>
      </div>
      {triggerFilter && (
        <div className="flex flex-wrap items-center gap-2 text-sm text-default-500">
          <Chip color="primary" size="sm" variant="flat" onClose={clearTriggerFilter}>
            {t("workflow.filter.trigger", {
              name: triggerDisplayName(t, triggerFilter, triggerNameByKind.get(triggerFilter)),
            })}
          </Chip>
          <Button size="sm" variant="light" onPress={clearTriggerFilter}>
            {t("workflow.filter.clear")}
          </Button>
        </div>
      )}
      <Tabs
        aria-label={t("workflow.sections.label")}
        selectedKey={section}
        onSelectionChange={(key) => setSelection({ filter: triggerFilter, section: String(key) })}
      >
        {triggerFilter && (
          <Tab key="all" title={t("workflow.sections.all", { count: filteredWorkflows.length })} />
        )}
        <Tab
          key="custom"
          title={t("workflow.sections.custom", {
            count: filteredWorkflows.filter((wf) => !wf.isBuiltin).length,
          })}
        />
        <Tab
          key="builtin"
          title={t("workflow.sections.builtin", {
            count: filteredWorkflows.filter((wf) => wf.isBuiltin).length,
          })}
        />
      </Tabs>
      <p className="text-sm leading-relaxed text-default-500">
        {t(`workflow.sections.${section}Hint`)}
      </p>

      {triggersFailed && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-warning-600" role="alert">
          <span>{t("workflow.list.triggersFailed")}</span>
          <Button size="sm" variant="light" onPress={() => void reloadTriggers()}>
            {t("workflow.diagnostics.retry")}
          </Button>
        </div>
      )}
      {loading ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
        </div>
      ) : loadFailed ? (
        <div
          className="flex flex-col items-center gap-3 py-12 text-sm text-default-500"
          role="alert"
        >
          <p>{t("workflow.list.loadFailed")}</p>
          <Button
            size="sm"
            startContent={<ReloadOutlined />}
            variant="flat"
            onPress={() => void load()}
          >
            {t("workflow.diagnostics.retry")}
          </Button>
        </div>
      ) : displayedWorkflows.length === 0 ? (
        <div className="flex flex-col items-center gap-3 py-12 text-sm text-default-500">
          <p>{t(`workflow.sections.${section}Empty`)}</p>
          {section === "custom" && (
            <Button size="sm" variant="flat" onPress={handleAddFromTemplate}>
              {t("workflow.templates.title")}
            </Button>
          )}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {displayedWorkflows.map((wf) => {
            const triggerUi = getWorkflowTriggerUI(wf.triggerKind);
            const FilterSummary = triggerUi?.FilterSummary;
            const filter = triggerUi ? triggerUi.parseFilter(wf.triggerFilterJson) : null;
            const managedRun = triggerByKind.get(wf.triggerKind)?.supportsManualRun === false;
            const runEntry = triggerUi?.runEntry;
            const guide = workflowPresetGuide(wf);
            const validation = checks.getState(wf.id);
            const needsConfiguration =
              !managedRun && hasWorkflowConfigurationErrors(validation.result);

            return (
              <div
                key={wf.id}
                className="border border-default-200 rounded-lg p-3 flex flex-wrap items-center gap-3"
                data-workflow-id={wf.id}
              >
                <Switch
                  aria-label={t("workflow.field.enabled")}
                  isSelected={wf.enabled}
                  size="sm"
                  onValueChange={(v) => handleToggleEnabled(wf, v)}
                />

                <div className="flex-1 min-w-0 flex flex-col gap-1">
                  <div className="flex flex-wrap items-center gap-2 min-w-0">
                    <span className="font-medium truncate">{workflowLabel(wf, t)}</span>
                    <WorkflowTriggerBadge
                      trigger={triggerByKind.get(wf.triggerKind)}
                      triggerKind={wf.triggerKind}
                    />
                    <Chip color="default" size="sm" variant="flat">
                      {t<string>("workflow.activity.count", { count: wf.activities.length })}
                    </Chip>
                  </div>
                  {workflowDescription(wf, t) && (
                    <p className="line-clamp-3 whitespace-pre-wrap break-words text-xs text-default-500">
                      {workflowDescription(wf, t)}
                    </p>
                  )}
                  {FilterSummary && filter && !managedRun && <FilterSummary filter={filter} />}
                  <details className="mt-1 rounded-lg bg-default-50 p-3">
                    <summary className="w-fit cursor-pointer text-xs font-medium text-default-600">
                      {t("workflow.usage.title")}
                    </summary>
                    <div className="mt-3 flex flex-col gap-3">
                      <TriggerUsageSummary
                        compact
                        showActions={false}
                        trigger={triggerByKind.get(wf.triggerKind)}
                        triggerKind={wf.triggerKind}
                      />
                      {guide && <PresetUsage guide={guide} />}
                    </div>
                  </details>
                  <div className="flex flex-wrap gap-1.5 mt-1">
                    {/* Tiny chain preview — kind chips in order. */}
                    {wf.activities.map((a, i) => (
                      <Chip key={i} size="sm" variant="flat">
                        {activityDisplayName(t, a.kind)}
                      </Chip>
                    ))}
                  </div>
                  <div className="text-xs text-default-400 flex flex-wrap gap-x-3 mt-1">
                    <span>
                      {t<string>("workflow.status.lastRun")}:{" "}
                      {formatTime(wf.lastRunAt) ?? t<string>("workflow.status.lastRunNever")}
                    </span>
                  </div>
                  <div className="mt-1">
                    <WorkflowDiagnostics
                      failed={validation.failed}
                      loading={validation.loading}
                      result={validation.result}
                      onCheck={() => checks.retry(wf.id)}
                    />
                  </div>
                  {wf.lastError && (
                    <div className="text-xs text-danger">
                      {t<string>("workflow.status.error")}: {wf.lastError}
                    </div>
                  )}
                </div>

                <div className="flex flex-wrap items-center gap-1">
                  <Button
                    aria-label={t(
                      managedRun
                        ? (runEntry?.labelKey ?? "workflow.entry.managed")
                        : needsConfiguration
                          ? "workflow.diagnostics.configure"
                          : "workflow.manualRun.tooltip",
                    )}
                    color="primary"
                    isDisabled={!triggerByKind.has(wf.triggerKind) || (managedRun && !runEntry)}
                    isIconOnly={!managedRun && !needsConfiguration}
                    size="sm"
                    title={t<string>(
                      managedRun
                        ? (runEntry?.labelKey ?? "workflow.entry.managed")
                        : needsConfiguration
                          ? "workflow.diagnostics.configure"
                          : "workflow.manualRun.tooltip",
                    )}
                    variant="light"
                    onPress={() => handleRun(wf)}
                  >
                    {managedRun ? (
                      <ArrowRightOutlined className="text-lg" />
                    ) : needsConfiguration ? (
                      <EditOutlined className="text-lg" />
                    ) : (
                      <PlayCircleOutlined className="text-lg" />
                    )}
                    {managedRun && t(runEntry?.labelKey ?? "workflow.entry.managed")}
                    {needsConfiguration && t("workflow.diagnostics.configure")}
                  </Button>
                  <Button
                    isIconOnly
                    aria-label={t<string>("workflow.runs.openTooltip")}
                    size="sm"
                    title={t<string>("workflow.runs.openTooltip")}
                    variant="light"
                    onPress={() => setRunsDrawerFor(wf)}
                  >
                    <HistoryOutlined className="text-lg" />
                  </Button>
                  <Button
                    isIconOnly
                    aria-label={t(
                      wf.isBuiltin ? "workflow.templates.configure" : "workflow.action.edit",
                    )}
                    size="sm"
                    variant="light"
                    onPress={() => handleEdit(wf)}
                  >
                    <EditOutlined className="text-lg" />
                  </Button>
                  {!wf.isBuiltin && (
                    <Button
                      isIconOnly
                      aria-label={t("workflow.action.delete")}
                      color="danger"
                      size="sm"
                      variant="light"
                      onPress={() => handleDelete(wf)}
                    >
                      <DeleteOutlined className="text-lg" />
                    </Button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {runsDrawerFor && (
        <WorkflowRunsDrawer
          isOpen
          triggerKind={runsDrawerFor.triggerKind}
          workflowDefinitionId={runsDrawerFor.id}
          workflowName={workflowLabel(runsDrawerFor, t)}
          onClose={() => setRunsDrawerFor(null)}
        />
      )}
    </div>
  );
};

export default WorkflowPage;
