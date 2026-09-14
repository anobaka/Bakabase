"use client";

import type { components } from "@/sdk/BApi2";
import type { WorkflowValidation } from "@/components/Workflow/metadata";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  ArrowRightOutlined,
  CheckCircleOutlined,
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
import WorkflowDiagnostics from "@/components/Workflow/WorkflowDiagnostics";
import TemplateLibrary from "@/components/Workflow/TemplateLibrary";
import { PresetUsage, workflowPresetGuide } from "@/components/Workflow/presetGuides";
import BApi from "@/sdk/BApi";
import { Button, Chip, Modal, Spinner, Switch, Tab, Tabs, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type WorkflowVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];
type TriggerDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];

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
  const { createPortal } = useBakabaseContext();

  const [workflows, setWorkflows] = useState<WorkflowVm[]>([]);
  const [triggers, setTriggers] = useState<TriggerDescriptorVm[]>([]);
  const [loading, setLoading] = useState(true);
  const [section, setSection] = useState("custom");
  const [validationFor, setValidationFor] = useState<{
    id: number;
    result?: WorkflowValidation;
    loading?: boolean;
    failed?: boolean;
  }>();
  const checkRevision = useRef(0);

  useEffect(
    () => () => {
      checkRevision.current += 1;
    },
    [],
  );

  const checkWorkflow = async (id: number) => {
    const revision = ++checkRevision.current;

    setValidationFor({ id, loading: true });
    try {
      const rsp = await BApi.workflow.validateSavedWorkflow(id);

      if (revision !== checkRevision.current) return;
      if (rsp.code || !rsp.data) throw new Error("Workflow validation failed");
      setValidationFor({ id, result: rsp.data });
    } catch {
      if (revision === checkRevision.current) setValidationFor({ id, failed: true });
    }
  };
  // Single drawer instance — opening for a different workflow replaces the
  // selection. Using createPortal would spawn a new component per click.
  const [runsDrawerFor, setRunsDrawerFor] = useState<WorkflowVm | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [wfRsp, trigRsp] = await Promise.all([
        BApi.workflow.searchWorkflows({}),
        BApi.workflow.getWorkflowTriggers(),
      ]);

      setWorkflows((wfRsp.data ?? []) as WorkflowVm[]);
      setTriggers((trigRsp.data ?? []) as TriggerDescriptorVm[]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
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
  const displayedWorkflows = workflows.filter((wf) =>
    section === "builtin" ? wf.isBuiltin : !wf.isBuiltin,
  );

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
      <Tabs
        aria-label={t("workflow.sections.label")}
        selectedKey={section}
        onSelectionChange={(key) => setSection(String(key))}
      >
        <Tab
          key="custom"
          title={t("workflow.sections.custom", {
            count: workflows.filter((wf) => !wf.isBuiltin).length,
          })}
        />
        <Tab
          key="builtin"
          title={t("workflow.sections.builtin", {
            count: workflows.filter((wf) => wf.isBuiltin).length,
          })}
        />
      </Tabs>
      <p className="text-sm leading-relaxed text-default-500">
        {t(`workflow.sections.${section}Hint`)}
      </p>

      {loading ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
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

            return (
              <div
                key={wf.id}
                className="border border-default-200 rounded-lg p-3 flex flex-wrap items-center gap-3"
              >
                <Switch
                  aria-label={t("workflow.field.enabled")}
                  isSelected={wf.enabled}
                  size="sm"
                  onValueChange={(v) => handleToggleEnabled(wf, v)}
                />

                <div className="flex-1 min-w-0 flex flex-col gap-1">
                  <div className="flex items-center gap-2 min-w-0">
                    <span className="font-medium truncate">{workflowLabel(wf, t)}</span>
                    <Chip color="default" size="sm" variant="flat">
                      {triggerDisplayName(t, wf.triggerKind, triggerNameByKind.get(wf.triggerKind))}
                    </Chip>
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
                  {managedRun && runEntry && (
                    <p className="text-xs text-primary">{t(runEntry.descriptionKey)}</p>
                  )}
                  {guide && (
                    <details className="mt-1 rounded-lg bg-default-50 p-3">
                      <summary className="w-fit cursor-pointer text-xs font-medium text-default-600">
                        {t("workflow.usage.title")}
                      </summary>
                      <div className="mt-3">
                        <PresetUsage guide={guide} />
                      </div>
                    </details>
                  )}
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
                  {validationFor?.id === wf.id && (
                    <div className="mt-2">
                      <WorkflowDiagnostics
                        failed={validationFor.failed}
                        loading={validationFor.loading}
                        result={validationFor.result}
                        onCheck={() => void checkWorkflow(wf.id)}
                      />
                    </div>
                  )}
                  {wf.lastError && (
                    <div className="text-xs text-danger">
                      {t<string>("workflow.status.error")}: {wf.lastError}
                    </div>
                  )}
                </div>

                <div className="flex flex-wrap items-center gap-1">
                  <Button
                    isIconOnly
                    aria-label={t<string>("workflow.diagnostics.check")}
                    size="sm"
                    title={t<string>("workflow.diagnostics.check")}
                    variant="light"
                    onPress={() => void checkWorkflow(wf.id)}
                  >
                    <CheckCircleOutlined className="text-lg" />
                  </Button>
                  <Button
                    aria-label={t(
                      managedRun
                        ? (runEntry?.labelKey ?? "workflow.entry.managed")
                        : "workflow.manualRun.tooltip",
                    )}
                    color="primary"
                    isDisabled={!triggerByKind.has(wf.triggerKind) || (managedRun && !runEntry)}
                    isIconOnly={!managedRun}
                    size="sm"
                    title={t<string>(
                      managedRun
                        ? (runEntry?.labelKey ?? "workflow.entry.managed")
                        : "workflow.manualRun.tooltip",
                    )}
                    variant="light"
                    onPress={() => handleRun(wf)}
                  >
                    {managedRun ? (
                      <ArrowRightOutlined className="text-lg" />
                    ) : (
                      <PlayCircleOutlined className="text-lg" />
                    )}
                    {managedRun && t(runEntry?.labelKey ?? "workflow.entry.managed")}
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
