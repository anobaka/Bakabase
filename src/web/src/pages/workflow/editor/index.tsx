"use client";

import type { EditorSeedLike } from "@/components/Workflow/CanvasEditor/types";
import type { components } from "@/sdk/BApi2";

import React, { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";

import WorkflowCanvasEditor from "@/components/Workflow/CanvasEditor";
import {
  EDITOR_TEMPLATES,
  seedToDrafts,
  takeStoredSeed,
} from "@/components/Workflow/CanvasEditor/templates";
import BApi from "@/sdk/BApi";
import { Spinner } from "@/components/bakaui";

type WorkflowVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];
type TriggerDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];

/**
 * /workflows/editor — the full-page canvas editor. `?id=` edits an existing definition,
 * `?template=` prefills a fresh one (e.g. fileCleaning), `?seed=1` consumes a one-shot
 * hand-off another page left in sessionStorage (File Name Modifier's "upgrade").
 */
const WorkflowEditorPage: React.FC = () => {
  const [params] = useSearchParams();
  const id = params.get("id");
  const templateId = params.get("template");
  const hasStoredSeed = params.get("seed") === "1";

  const [workflow, setWorkflow] = useState<WorkflowVm | null | undefined>(undefined);
  const [triggers, setTriggers] = useState<TriggerDescriptorVm[] | null>(null);

  // Resolved once on mount — the stored seed is one-shot and must not be re-read on rerenders.
  const seed = useMemo<EditorSeedLike | undefined>(() => {
    if (id) return undefined;
    const raw = hasStoredSeed ? takeStoredSeed() : templateId ? EDITOR_TEMPLATES[templateId] : null;

    if (!raw) return undefined;

    return {
      name: raw.name,
      nameKey: raw.nameKey,
      triggerKind: raw.triggerKind,
      drafts: seedToDrafts(raw),
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const [trigRsp, wfRsp] = await Promise.all([
        BApi.workflow.getWorkflowTriggers(),
        id ? BApi.workflow.getWorkflow(parseInt(id, 10)) : Promise.resolve(null),
      ]);

      if (cancelled) return;
      setTriggers((trigRsp.data ?? []) as TriggerDescriptorVm[]);
      setWorkflow(id ? ((wfRsp?.data ?? null) as WorkflowVm | null) : null);
    })();

    return () => {
      cancelled = true;
    };
  }, [id]);

  if (triggers === null || workflow === undefined) {
    return (
      <div className="flex justify-center py-16">
        <Spinner size="lg" />
      </div>
    );
  }

  return (
    <WorkflowCanvasEditor
      key={id ?? "new"}
      seed={seed}
      triggers={triggers}
      workflow={workflow ?? undefined}
    />
  );
};

export default WorkflowEditorPage;
