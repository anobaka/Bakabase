"use client";

import type { components } from "@/sdk/BApi2";

import React, { lazy, Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { SelectItem } from "@heroui/react";
import { useNavigate } from "react-router-dom";
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
import {
  triggerSourceLabel,
  useWorkflowTriggerDescriptors,
} from "@/components/Workflow/triggerPresentation";
import { EDITOR_TEMPLATES, seedToDrafts } from "@/components/Workflow/CanvasEditor/templates";
import type { EditorSeedLike } from "@/components/Workflow/CanvasEditor/types";
import WorkflowTriggerBadge from "@/components/Workflow/WorkflowTriggerBadge";
import TriggerUsageSummary from "@/components/Workflow/TriggerUsageSummary";
import TemplateLibrary from "@/components/Workflow/TemplateLibrary";
import { PresetUsage, workflowPresetGuide } from "@/components/Workflow/presetGuides";
import BApi from "@/sdk/BApi";
import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Input,
  Modal,
  Select,
  Spinner,
  Switch,
  Tab,
  Tabs,
  toast,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type WorkflowVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];

const WorkflowCanvasEditor = lazy(() => import("@/components/Workflow/CanvasEditor"));

function formatTime(iso?: string | null): string | null {
  if (!iso) return null;
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

export interface WorkflowBrowserProps {
  /** A fixed source-action scope, independent of the workflow page URL. */
  triggerKind?: string;
  /** Page-controlled event filter; omitting it enables local filtering. */
  triggerFilter?: string | null;
  onTriggerFilterChange?: (kind: string | null) => void;
  /** Reuse the real editor inside the host modal instead of leaving its source action. */
  embedded?: boolean;
  onChanged?: () => void;
}

const WorkflowBrowser: React.FC<WorkflowBrowserProps> = ({
  triggerKind,
  triggerFilter: suppliedFilter,
  onTriggerFilterChange,
  embedded = false,
  onChanged,
}) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [localTrigger, setLocalTrigger] = useState<string | null>(null);
  const triggerFilter = triggerKind ?? suppliedFilter ?? localTrigger;
  const [sourceFilter, setSourceFilter] = useState("");
  const [keyword, setKeyword] = useState("");
  const [enabledFilter, setEnabledFilter] = useState("all");
  const [editor, setEditor] = useState<{ workflow?: WorkflowVm; seed?: EditorSeedLike } | null>(
    null,
  );
  const { createPortal } = useBakabaseContext();

  const server = BApi.baseUrl;
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
  }, [server]);

  useEffect(() => {
    void load();

    return () => loadController.current?.abort();
  }, [load]);

  useEffect(() => {
    setEditor(null);
  }, [server]);

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
      handleEdit(wf);

      return;
    }

    if (trigger.requiresManualPayload || wf.parameters?.length) {
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

  const handleAdd = () =>
    embedded
      ? setEditor({ seed: triggerKind ? { triggerKind, drafts: [] } : undefined })
      : navigate("/workflows/editor");
  const handleDestination = (path: string) => {
    if (!embedded) {
      navigate(path);
      return;
    }
    const query = new URL(path, window.location.origin).searchParams;
    const id = Number(query.get("id"));
    const template = EDITOR_TEMPLATES[query.get("template") ?? ""];
    if (id) {
      const workflow = workflows.find((item) => item.id === id);
      if (workflow) setEditor({ workflow });
    } else if (template && (!triggerKind || template.triggerKind === triggerKind)) {
      setEditor({ seed: { ...template, drafts: seedToDrafts(template) } });
    }
  };
  const handleAddFromTemplate = () =>
    createPortal(TemplateLibrary, {
      workflows,
      onChoose: embedded ? handleDestination : navigate,
      triggerKind,
    });
  const handleEdit = (wf: WorkflowVm) =>
    embedded ? setEditor({ workflow: wf }) : navigate(`/workflows/editor?id=${wf.id}`);
  const handleDelete = (wf: WorkflowVm) =>
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("workflow.delete.confirm"),
      children: workflowLabel(wf, t),
      onOk: async () => {
        await BApi.workflow.deleteWorkflow(wf.id);
        await load();
        onChanged?.();
      },
    });
  const handleToggleEnabled = async (wf: WorkflowVm, next: boolean) => {
    const response = await BApi.workflow.patchWorkflow(wf.id, { enabled: next });

    if (response.code) return;
    setWorkflows((arr) => arr.map((w) => (w.id === wf.id ? { ...w, enabled: next } : w)));
    onChanged?.();
  };
  const scopedTriggers = triggerKind ? triggers.filter((tr) => tr.kind === triggerKind) : triggers;
  const sourceOptions = [...new Map(scopedTriggers.map((tr) => [tr.sourceModule, tr])).values()]
    .filter((tr) => !!tr.sourceModule)
    .sort((a, b) => triggerSourceLabel(a, t).localeCompare(triggerSourceLabel(b, t)));
  const eventOptions = scopedTriggers.filter(
    (tr) => !sourceFilter || tr.sourceModule === sourceFilter,
  );
  const filteredWorkflows = workflows.filter(
    (wf) =>
      (!triggerFilter || wf.triggerKind === triggerFilter) &&
      (!sourceFilter || triggerByKind.get(wf.triggerKind)?.sourceModule === sourceFilter) &&
      (enabledFilter === "all" || wf.enabled === (enabledFilter === "enabled")) &&
      (!keyword.trim() ||
        `${workflowLabel(wf, t)} ${workflowDescription(wf, t) ?? ""}`
          .toLocaleLowerCase()
          .includes(keyword.trim().toLocaleLowerCase())),
  );
  const displayedWorkflows = filteredWorkflows.filter(
    (wf) => section === "all" || (section === "builtin" ? wf.isBuiltin : !wf.isBuiltin),
  );
  const changeTriggerFilter = (kind: string | null) => {
    if (triggerKind) return;
    if (onTriggerFilterChange) onTriggerFilterChange(kind);
    else setLocalTrigger(kind);
  };
  const clearTriggerFilter = () => changeTriggerFilter(null);

  if (editor)
    return (
      <Suspense
        fallback={
          <div className="flex justify-center py-10">
            <Spinner />
          </div>
        }
      >
        <WorkflowCanvasEditor
          embedded
          seed={editor.seed}
          triggers={triggerKind ? scopedTriggers : triggers}
          workflow={editor.workflow}
          onBack={() => setEditor(null)}
          onSaved={async () => {
            setEditor(null);
            await load();
            onChanged?.();
          }}
        />
      </Suspense>
    );

  return (
    <div
      className={`flex min-w-0 flex-col gap-3 ${embedded ? "p-1" : "p-4"}`}
      data-workflow-browser
    >
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
      {triggerFilter && !triggerKind && (
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
      <div className="grid grid-cols-1 gap-3 rounded-xl bg-default-50 p-3 sm:grid-cols-2 xl:grid-cols-4">
        <Select
          aria-label={t("workflow.browser.filter.source")}
          label={t("workflow.browser.filter.source")}
          labelPlacement="outside"
          className="min-w-0"
          variant="bordered"
          size="sm"
          disallowEmptySelection
          selectedKeys={[sourceFilter ? `source:${sourceFilter}` : "all"]}
          onSelectionChange={(keys) => {
            const key = String(Array.from(keys)[0] ?? "all");
            const source = key === "all" ? "" : key.slice("source:".length);
            setSourceFilter(source);
            if (
              !triggerKind &&
              triggerFilter &&
              source &&
              triggerByKind.get(triggerFilter)?.sourceModule !== source
            )
              changeTriggerFilter(null);
          }}
        >
          <SelectItem key="all">{t("workflow.browser.filter.allSources")}</SelectItem>
          {sourceOptions.map((tr) => (
            <SelectItem key={`source:${tr.sourceModule}`}>{triggerSourceLabel(tr, t)}</SelectItem>
          ))}
        </Select>
        <Select
          aria-label={t("workflow.browser.filter.event")}
          label={t("workflow.browser.filter.event")}
          labelPlacement="outside"
          className="min-w-0"
          variant="bordered"
          size="sm"
          disallowEmptySelection
          isDisabled={!!triggerKind}
          selectedKeys={[triggerFilter ? `event:${triggerFilter}` : "all"]}
          onSelectionChange={(keys) => {
            const key = String(Array.from(keys)[0] ?? "all");
            changeTriggerFilter(key === "all" ? null : key.slice("event:".length));
          }}
        >
          {!triggerKind && (
            <SelectItem key="all">{t("workflow.browser.filter.allEvents")}</SelectItem>
          )}
          {triggerFilter && !eventOptions.some((tr) => tr.kind === triggerFilter) && (
            <SelectItem key={`event:${triggerFilter}`}>
              {triggerDisplayName(t, triggerFilter, triggerNameByKind.get(triggerFilter))}
            </SelectItem>
          )}
          {eventOptions.map((tr) => (
            <SelectItem key={`event:${tr.kind}`}>
              {triggerDisplayName(t, tr.kind, tr.displayName)}
            </SelectItem>
          ))}
        </Select>
        <Input
          aria-label={t("workflow.browser.filter.keyword")}
          label={t("workflow.browser.filter.keyword")}
          labelPlacement="outside"
          className="min-w-0"
          variant="bordered"
          size="sm"
          type="search"
          value={keyword}
          placeholder={t("workflow.browser.filter.keywordHint")}
          onValueChange={setKeyword}
        />
        <Select
          aria-label={t("workflow.browser.filter.enabled")}
          label={t("workflow.browser.filter.enabled")}
          labelPlacement="outside"
          className="min-w-0"
          variant="bordered"
          size="sm"
          disallowEmptySelection
          selectedKeys={[enabledFilter]}
          onSelectionChange={(keys) => setEnabledFilter(String(Array.from(keys)[0] ?? "all"))}
        >
          <SelectItem key="all">{t("workflow.browser.filter.anyStatus")}</SelectItem>
          <SelectItem key="enabled">{t("workflow.browser.filter.enabledOnly")}</SelectItem>
          <SelectItem key="disabled">{t("workflow.browser.filter.disabledOnly")}</SelectItem>
        </Select>
      </div>
      <Tabs
        aria-label={t("workflow.sections.label")}
        selectedKey={section}
        onSelectionChange={(key) => setSelection({ filter: triggerFilter, section: String(key) })}
      >
        <Tab key="all" title={t("workflow.sections.all", { count: filteredWorkflows.length })} />
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
                  <Accordion
                    className="mt-1 rounded-lg bg-default-50 px-3"
                    isCompact
                    keepContentMounted
                  >
                    <AccordionItem
                      key="usage"
                      title={t("workflow.usage.title")}
                      classNames={{ title: "text-xs font-medium text-default-600" }}
                    >
                      <div className="flex flex-col gap-3">
                        <TriggerUsageSummary
                          compact
                          showActions={false}
                          trigger={triggerByKind.get(wf.triggerKind)}
                          triggerKind={wf.triggerKind}
                        />
                        {guide && <PresetUsage guide={guide} />}
                      </div>
                    </AccordionItem>
                  </Accordion>
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

export default WorkflowBrowser;
