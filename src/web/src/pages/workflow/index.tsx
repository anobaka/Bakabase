"use client";

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  EditOutlined,
  HistoryOutlined,
  PlusCircleOutlined,
} from "@ant-design/icons";

import WorkflowEditor from "@/components/Workflow/WorkflowEditor";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import { getWorkflowTriggerUI } from "@/components/Workflow/Triggers";
import { activityDisplayName, triggerDisplayName } from "@/components/Workflow/displayNames";
import BApi from "@/sdk/BApi";
import { Button, Chip, Modal, Spinner, Switch } from "@/components/bakaui";
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
  const { createPortal } = useBakabaseContext();

  const [workflows, setWorkflows] = useState<WorkflowVm[]>([]);
  const [triggers, setTriggers] = useState<TriggerDescriptorVm[]>([]);
  const [loading, setLoading] = useState(true);
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

  const handleAdd = () => createPortal(WorkflowEditor, { triggers, onSaved: load });
  const handleEdit = (wf: WorkflowVm) =>
    createPortal(WorkflowEditor, { workflow: wf, triggers, onSaved: load });
  const handleDelete = (wf: WorkflowVm) =>
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("workflow.delete.confirm"),
      children: wf.name,
      onOk: async () => {
        await BApi.workflow.deleteWorkflow(wf.id);
        await load();
      },
    });
  const handleToggleEnabled = async (wf: WorkflowVm, next: boolean) => {
    await BApi.workflow.patchWorkflow(wf.id, { enabled: next });
    setWorkflows((arr) => arr.map((w) => (w.id === wf.id ? { ...w, enabled: next } : w)));
  };

  return (
    <div className="flex flex-col gap-3 p-4">
      <div className="flex items-center gap-2">
        <h2 className="text-lg font-semibold">{t<string>("workflow.title")}</h2>
        <Button
          color="primary"
          size="sm"
          startContent={<PlusCircleOutlined />}
          onPress={handleAdd}
        >
          {t<string>("workflow.action.add")}
        </Button>
      </div>

      {loading ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
        </div>
      ) : workflows.length === 0 ? (
        <div className="text-center text-default-500 py-10">
          {t<string>("workflow.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {workflows.map((wf) => {
            const triggerUi = getWorkflowTriggerUI(wf.triggerKind);
            const FilterSummary = triggerUi?.FilterSummary;
            const filter = triggerUi ? triggerUi.parseFilter(wf.triggerFilterJson) : null;

            return (
              <div
                key={wf.id}
                className="border border-default-200 rounded-lg p-3 flex items-center gap-3"
              >
                <Switch
                  isSelected={wf.enabled}
                  size="sm"
                  onValueChange={(v) => handleToggleEnabled(wf, v)}
                />

                <div className="flex-1 min-w-0 flex flex-col gap-1">
                  <div className="flex items-center gap-2 min-w-0">
                    <span className="font-medium truncate">{wf.name}</span>
                    <Chip color="default" size="sm" variant="flat">
                      {triggerDisplayName(t, wf.triggerKind, triggerNameByKind.get(wf.triggerKind))}
                    </Chip>
                    <Chip color="default" size="sm" variant="flat">
                      {t<string>("workflow.activity.count", { count: wf.activities.length })}
                    </Chip>
                  </div>
                  {FilterSummary && filter && <FilterSummary filter={filter} />}
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
                  {wf.lastError && (
                    <div className="text-xs text-danger">
                      {t<string>("workflow.status.error")}: {wf.lastError}
                    </div>
                  )}
                </div>

                <div className="flex items-center gap-1">
                  <Button
                    isIconOnly
                    size="sm"
                    title={t<string>("workflow.runs.openTooltip")}
                    variant="light"
                    onPress={() => setRunsDrawerFor(wf)}
                  >
                    <HistoryOutlined className="text-lg" />
                  </Button>
                  <Button isIconOnly size="sm" variant="light" onPress={() => handleEdit(wf)}>
                    <EditOutlined className="text-lg" />
                  </Button>
                  <Button
                    isIconOnly
                    color="danger"
                    size="sm"
                    variant="light"
                    onPress={() => handleDelete(wf)}
                  >
                    <DeleteOutlined className="text-lg" />
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {runsDrawerFor && (
        <WorkflowRunsDrawer
          isOpen
          workflowDefinitionId={runsDrawerFor.id}
          workflowName={runsDrawerFor.name}
          onClose={() => setRunsDrawerFor(null)}
        />
      )}
    </div>
  );
};

export default WorkflowPage;
