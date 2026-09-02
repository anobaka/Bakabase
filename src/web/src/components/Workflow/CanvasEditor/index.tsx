"use client";

import type { PaletteEntry } from "./NodePalette";
import type { ActivityDraft, CanvasSelection, EditorSeedLike, SlotFit } from "./types";
import type { components } from "@/sdk/BApi2";

import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  AiOutlineArrowLeft,
  AiOutlineExpand,
  AiOutlineHistory,
  AiOutlineMinus,
  AiOutlinePlayCircle,
  AiOutlinePlus,
} from "react-icons/ai";

import CanvasNode from "./CanvasNode";
import InspectorPanel from "./InspectorPanel";
import NodePalette from "./NodePalette";
import { useCanvasView } from "./useCanvasView";
import { useChainDrag } from "./useChainDrag";
import { getWorkflowActivityUI } from "../Activities";
import { getWorkflowTriggerUI } from "../Triggers";
import ItemTypePill from "../ItemTypePill";
import ManualRunModal from "../ManualRunModal";
import WorkflowRunsDrawer from "../WorkflowRunsDrawer";
import { activityDisplayName, triggerDisplayName } from "../displayNames";
import { classifyActivity } from "../activityFit";
import { descriptorAccepts, walkChain } from "../chainWalk";
import { WorkflowItemTypeIndex } from "../itemTypeRegistry";

import BApi from "@/sdk/BApi";
import { Button, Input, Spinner, Switch, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { WorkflowActivityErrorBehavior } from "@/sdk/constants";

type WorkflowVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];
type ActivityVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowActivityViewModel"];
type TriggerDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];
type ActivityDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowActivityDescriptorViewModel"];

/** Hidden from the palette; auto-inserted (with a pinned target type) for bridge drops. */
const AI_TRANSFORM_KIND = "transform.ai.transform";

interface Props {
  /** Existing definition to edit; absent = creating. */
  workflow?: WorkflowVm;
  triggers: TriggerDescriptorVm[];
  /** Prefill for a fresh definition (template / hand-off). Ignored when editing. */
  seed?: EditorSeedLike;
}

function activityVmToDraft(a: ActivityVm): ActivityDraft {
  return {
    clientId: crypto.randomUUID(),
    kind: a.kind,
    configJson: a.configJson,
    onItemError: a.onItemError as WorkflowActivityErrorBehavior,
  };
}

/**
 * The full-page three-zone workflow editor (design: docs/workflow-editor-redesign.html):
 * node palette on the left, the chain as a horizontal canvas rail in the middle with
 * magnetic insertion slots, and a fixed inspector on the right. All type inference and every
 * config form is the same code the modal editor used — only the shell changed.
 */
const WorkflowCanvasEditor: React.FC<Props> = ({ workflow, triggers, seed }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();
  const isEditing = !!workflow;

  const supportedTriggers = useMemo(
    () => triggers.filter((tr) => !!getWorkflowTriggerUI(tr.kind)),
    [triggers],
  );
  const initialTriggerKind =
    workflow?.triggerKind ?? seed?.triggerKind ?? supportedTriggers[0]?.kind ?? "";

  const [name, setName] = useState(
    workflow?.name ?? seed?.name ?? (seed?.nameKey ? t<string>(seed.nameKey) : ""),
  );
  const [triggerKind, setTriggerKind] = useState(initialTriggerKind);
  const [enabled, setEnabled] = useState(workflow?.enabled ?? true);
  const triggerUi = useMemo(() => getWorkflowTriggerUI(triggerKind), [triggerKind]);
  const [filter, setFilter] = useState<unknown>(() => {
    if (!triggerUi) return null;

    return workflow?.triggerFilterJson
      ? triggerUi.parseFilter(workflow.triggerFilterJson)
      : triggerUi.defaultFilter();
  });
  const [drafts, setDrafts] = useState<ActivityDraft[]>(
    () => workflow?.activities?.map(activityVmToDraft) ?? seed?.drafts ?? [],
  );
  const [selection, setSelection] = useState<CanvasSelection | null>("trigger");
  const [saving, setSaving] = useState(false);
  const [runsOpen, setRunsOpen] = useState(false);

  // Descriptors + item types, fetched once (static within a session).
  const [allDescriptors, setAllDescriptors] = useState<ActivityDescriptorVm[] | null>(null);
  const [itemTypes, setItemTypes] = useState<WorkflowItemTypeIndex | null>(null);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const [actRsp, typeRsp] = await Promise.all([
        BApi.workflow.getWorkflowActivities(),
        BApi.workflow.getWorkflowItemTypes(),
      ]);

      if (cancelled) return;
      setAllDescriptors((actRsp.data ?? []) as ActivityDescriptorVm[]);
      setItemTypes(new WorkflowItemTypeIndex((typeRsp.data ?? []) as never[]));
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const descriptorByKind = useMemo(
    () => new Map((allDescriptors ?? []).map((a) => [a.kind, a] as const)),
    [allDescriptors],
  );

  const startItemType = useMemo(
    () => (triggerUi && filter !== null ? triggerUi.resolveOutputItemType(filter) : null),
    [triggerUi, filter],
  );

  const chain = useMemo(
    () =>
      startItemType === null || itemTypes === null
        ? null
        : walkChain(
            startItemType,
            drafts.map((a) => ({ kind: a.kind, configJson: a.configJson })),
            descriptorByKind,
            itemTypes,
          ),
    [startItemType, drafts, descriptorByKind, itemTypes],
  );

  /** Type entering each slot position 0..n over the current chain. */
  const slotTypes = useMemo(() => {
    if (!startItemType || !chain) return null;
    const types = [startItemType];

    for (const step of chain.steps) types.push(step.typeAfter);

    return types;
  }, [startItemType, chain]);

  const aiAvailable = useMemo(
    () =>
      !!getWorkflowActivityUI(AI_TRANSFORM_KIND) &&
      (allDescriptors ?? []).some((d) => d.kind === AI_TRANSFORM_KIND),
    [allDescriptors],
  );

  const itemTypeName = (tag: string) =>
    t<string>(`workflow.itemType.${tag}.displayName`, {
      defaultValue: itemTypes?.get(tag)?.displayName ?? tag,
    });

  // Palette: every activity (except the hidden AI bridge) grouped, with tail-fit / reason.
  const paletteEntries = useMemo<PaletteEntry[]>(() => {
    if (!allDescriptors || !chain || !itemTypes) return [];
    const tail = chain.typeAfter;

    return allDescriptors
      .filter((d) => d.kind !== AI_TRANSFORM_KIND)
      .filter((d) => !!getWorkflowActivityUI(d.kind))
      .map((d) => {
        const fitResult = classifyActivity(d, tail, itemTypes);
        const fit =
          fitResult === null || (fitResult.fit === "bridge" && !aiAvailable)
            ? null
            : fitResult.fit;

        return {
          descriptor: d,
          fit,
          reason:
            fit === null
              ? t<string>("workflow.editor.palette.incompatibleReason", {
                  current: itemTypeName(tail),
                  accepts: (d.acceptedInputItemTypes ?? [])
                    .map((x) => itemTypeName(x))
                    .concat(d.acceptedItemInterface ? [d.acceptedItemInterface] : [])
                    .join(" / "),
                })
              : undefined,
        };
      });
  }, [allDescriptors, chain, itemTypes, aiAvailable, t]);

  const newDraft = (kind: string, configJson?: string): ActivityDraft => {
    const ui = getWorkflowActivityUI(kind);

    return {
      clientId: crypto.randomUUID(),
      kind,
      configJson: configJson ?? (ui ? ui.serializeConfig(ui.defaultConfig()) : "{}"),
      onItemError: WorkflowActivityErrorBehavior.Fail,
    };
  };

  const bridgeDraft = (targetItemType: string): ActivityDraft | null => {
    const aiUi = getWorkflowActivityUI(AI_TRANSFORM_KIND);

    if (!aiUi) return null;

    return {
      clientId: crypto.randomUUID(),
      kind: AI_TRANSFORM_KIND,
      configJson: aiUi.serializeConfig({
        ...(aiUi.defaultConfig() as object),
        targetItemType,
      } as never),
      onItemError: WorkflowActivityErrorBehavior.Fail,
    };
  };

  /** Insert a palette node at a slot, adding the AI bridge in front when needed. */
  const insertAt = (kind: string, slot: number, fit: Exclude<SlotFit, null>) => {
    const descriptor = descriptorByKind.get(kind);

    setDrafts((arr) => {
      const next = [...arr];
      const inserted: ActivityDraft[] = [];

      if (fit === "bridge") {
        const target = descriptor?.acceptedInputItemTypes?.[0];
        const ai = target ? bridgeDraft(target) : null;

        if (ai) inserted.push(ai);
      }
      inserted.push(newDraft(kind));
      next.splice(slot, 0, ...inserted);
      setSelection(slot + inserted.length - 1);

      return next;
    });
  };

  const moveTo = (fromIdx: number, slot: number) => {
    setDrafts((arr) => {
      if (slot === fromIdx || slot === fromIdx + 1) return arr;
      const next = [...arr];
      const [node] = next.splice(fromIdx, 1);
      const to = slot > fromIdx ? slot - 1 : slot;

      next.splice(to, 0, node!);
      setSelection(to);

      return next;
    });
  };

  const removeAt = (idx: number) => {
    setDrafts((arr) => arr.filter((_, i) => i !== idx));
    setSelection((sel) =>
      typeof sel === "number" ? (sel === idx ? null : sel > idx ? sel - 1 : sel) : sel,
    );
  };

  const drag = useChainDrag({
    // Palette drops magnetize only onto slots the node fits (validity IS the magnetism);
    // moving an existing node is always allowed — the chain walk flags any resulting
    // incompatibility in red, same as the previous editor's reorder behavior.
    getSlotFit: (kind, slotIdx, fromIdx): SlotFit => {
      if (fromIdx != null) return "direct";
      if (!slotTypes || !itemTypes) return null;
      const descriptor = descriptorByKind.get(kind);
      const typeBefore = slotTypes[slotIdx];

      if (!descriptor || typeBefore === undefined) return null;
      if (descriptorAccepts(descriptor, typeBefore, itemTypes)) return "direct";
      if ((descriptor.acceptedInputItemTypes ?? []).length === 1 && aiAvailable) return "bridge";

      return null;
    },
    onDrop: (dragState, slotIdx, fit, removed) => {
      if (removed && dragState.fromIdx != null) {
        removeAt(dragState.fromIdx);

        return;
      }
      if (slotIdx == null || fit == null) return;
      if (dragState.fromIdx == null) insertAt(dragState.kind, slotIdx, fit);
      else moveTo(dragState.fromIdx, slotIdx);
    },
    onNodeClick: (idx) => setSelection(idx),
    onPaletteClick: (kind) => {
      const entry = paletteEntries.find((x) => x.descriptor.kind === kind);

      if (entry?.fit) insertAt(kind, drafts.length, entry.fit);
    },
  });

  const canvasView = useCanvasView(drag.canvasRef, allDescriptors !== null && itemTypes !== null);

  // ---- validation (same rules as before) ----
  const isNameValid = name.trim().length > 0;
  const isTriggerValid = triggerKind.length > 0 && !!triggerUi;
  const isActivityConfigsValid = drafts.every((a) => {
    const ui = getWorkflowActivityUI(a.kind);

    if (!ui) return false;
    try {
      return ui.isValid(ui.parseConfig(a.configJson));
    } catch {
      return false;
    }
  });
  const isChainValid = chain?.allCompatible ?? false;
  const isValid = isNameValid && isTriggerValid && isActivityConfigsValid && isChainValid;

  const lintMessage = !isNameValid
    ? t<string>("workflow.validation.nameRequired")
    : !isChainValid
      ? t<string>("workflow.editor.lint.chainBroken")
      : !isActivityConfigsValid
        ? t<string>("workflow.editor.lint.configIncomplete")
        : !drafts.some((a) => getWorkflowActivityUI(a.kind)?.category != null)
          ? drafts.length === 0
            ? t<string>("workflow.editor.lint.noActivities")
            : ""
          : "";

  const handleSave = async () => {
    if (!isValid || !triggerUi || saving) return;
    setSaving(true);
    const payload = {
      name,
      triggerFilterJson: triggerUi.serializeFilter(filter) ?? undefined,
      enabled,
      activities: drafts.map((a) => ({
        kind: a.kind,
        configJson: a.configJson,
        onItemError: a.onItemError,
      })),
    };

    try {
      if (isEditing) {
        await BApi.workflow.patchWorkflow(workflow!.id, payload);
      } else {
        const rsp = await BApi.workflow.addWorkflow({ ...payload, triggerKind });

        if (rsp.code) throw new Error(rsp.message ?? "save failed");
        // Stay in the editor, now editing the created definition.
        navigate(`/workflows/editor?id=${rsp.data!.id}`, { replace: true });
      }
      toast.success(t<string>("workflow.editor.saved"));
    } catch (e: any) {
      toast.danger(e?.message ?? String(e));
    } finally {
      setSaving(false);
    }
  };

  const handleRun = async () => {
    if (!workflow) return;
    const trigger = triggers.find((x) => x.kind === workflow.triggerKind);

    if (trigger?.requiresManualPayload) {
      createPortal(ManualRunModal, {
        workflowId: workflow.id,
        workflowName: name,
        trigger,
        onRan: () => setRunsOpen(true),
      });

      return;
    }
    const rsp = await BApi.workflow.runWorkflowManually(workflow.id, {});

    if (!rsp.code) {
      toast.success(t<string>("workflow.manualRun.started"));
      setRunsOpen(true);
    }
  };

  const onTriggerKindChange = (next: string) => {
    setTriggerKind(next);
    const ui = getWorkflowTriggerUI(next);

    setFilter(ui ? ui.defaultFilter() : null);
    setDrafts([]);
    setSelection("trigger");
  };

  if (allDescriptors === null || itemTypes === null) {
    return (
      <div className="flex justify-center py-16">
        <Spinner size="lg" />
      </div>
    );
  }

  const TriggerSummary = triggerUi?.FilterSummary;

  return (
    <div className="flex flex-col gap-0 h-[calc(100vh-16px)] min-h-0">
      {/* Top bar */}
      <div className="flex items-center gap-2 px-1 pb-2">
        <Button isIconOnly size="sm" variant="light" onPress={() => navigate("/workflows")}>
          <AiOutlineArrowLeft />
        </Button>
        <Input
          className="w-56"
          isInvalid={!isNameValid}
          placeholder={t<string>("workflow.field.name")}
          size="sm"
          value={name}
          onValueChange={setName}
        />
        <Switch isSelected={enabled} size="sm" onValueChange={setEnabled}>
          <span className="text-xs">{t<string>("workflow.field.enabled")}</span>
        </Switch>
        {lintMessage && <span className="text-xs text-warning">{lintMessage}</span>}
        <div className="flex-1" />
        {isEditing && (
          <>
            <Button
              size="sm"
              startContent={<AiOutlineHistory />}
              variant="flat"
              onPress={() => setRunsOpen(true)}
            >
              {t<string>("workflow.runs.title")}
            </Button>
            <Button
              size="sm"
              startContent={<AiOutlinePlayCircle />}
              variant="flat"
              onPress={handleRun}
            >
              {t<string>("workflow.manualRun.tooltip")}
            </Button>
          </>
        )}
        <Button
          color="primary"
          isDisabled={!isValid}
          isLoading={saving}
          size="sm"
          onPress={handleSave}
        >
          {t<string>("workflow.editor.save")}
        </Button>
      </div>

      {/* Three zones */}
      <div className="flex-1 min-h-0 grid grid-cols-[230px_1fr_300px] max-md:grid-cols-1 gap-0 border border-default-200 rounded-xl overflow-hidden">
        {/* Palette */}
        <div className="border-r border-default-200 max-md:border-r-0 max-md:border-b p-2 min-h-0">
          <NodePalette
            entries={paletteEntries}
            onAdd={(entry) =>
              entry.fit && insertAt(entry.descriptor.kind, drafts.length, entry.fit)
            }
            onDragStart={(ev, kind) => drag.startPaletteDrag(ev, kind)}
          />
        </div>

        {/* Canvas: a pan/zoom viewport — the chain lives on a transformed world layer, the
            dot grid follows the view, and dragging empty space pans. */}
        <div
          ref={drag.canvasRef}
          className="relative min-h-0 overflow-hidden touch-none cursor-grab"
          style={{
            backgroundImage:
              "radial-gradient(circle at 1px 1px, hsl(var(--heroui-default-300)) 1px, transparent 0)",
            backgroundSize: "22px 22px",
          }}
          onPointerDown={canvasView.onPanPointerDown}
        >
          <div ref={canvasView.worldRef} className="w-max origin-top-left will-change-transform">
          <div className="flex items-center min-h-[140px] w-max pr-8">
            {/* Trigger node */}
            <div
              className={`rounded-xl border-1.5 border-secondary/50 bg-content1 px-3 py-2 min-w-[150px] max-w-[220px]
                cursor-pointer select-none shrink-0 ${selection === "trigger" ? "ring-2 ring-primary/60" : ""}`}
              role="button"
              tabIndex={0}
              onClick={() => setSelection("trigger")}
              onKeyDown={(e) => e.key === "Enter" && setSelection("trigger")}
            >
              <div className="text-[10px] tracking-wide text-secondary">
                {t<string>("workflow.editor.category.trigger")}
              </div>
              <div className="text-[13px] font-semibold leading-tight">
                {triggerDisplayName(
                  t,
                  triggerKind,
                  triggers.find((x) => x.kind === triggerKind)?.displayName,
                )}
              </div>
              <div className="text-[10.5px] text-default-500 truncate max-w-[200px]">
                {TriggerSummary && filter != null ? <TriggerSummary filter={filter} /> : null}
              </div>
            </div>

            {startItemType && (
              <ItemTypePill horizontal index={itemTypes} itemType={startItemType} />
            )}

            <Slot active={drag.activeSlot?.index === 0} index={0} />

            {drafts.map((draft, i) => (
              <React.Fragment key={draft.clientId}>
                <CanvasNode
                  descriptor={descriptorByKind.get(draft.kind)}
                  dragSource={drag.dragging?.fromIdx === i}
                  draft={draft}
                  incompatible={chain ? !chain.steps[i]?.compatible : false}
                  index={i}
                  selected={selection === i}
                  onDelete={() => removeAt(i)}
                  onMove={(direction) => {
                    const to = direction === -1 ? i - 1 : i + 2;

                    if (to >= 0 && to <= drafts.length) moveTo(i, to);
                  }}
                  onPointerDown={(ev) => drag.startNodeDrag(ev, draft.kind, i)}
                  onSelect={() => setSelection(i)}
                />
                {chain && (
                  <ItemTypePill
                    horizontal
                    index={itemTypes}
                    invalid={i + 1 < drafts.length && !chain.steps[i + 1]?.compatible}
                    itemType={chain.steps[i].typeAfter}
                  />
                )}
                <Slot active={drag.activeSlot?.index === i + 1} index={i + 1} />
              </React.Fragment>
            ))}

            {/* End cap / empty-state guidance */}
            <div className="rounded-xl border-1.5 border-dashed border-default-300 px-3 py-2 text-[11.5px] text-default-500 min-w-[130px] max-w-[210px] shrink-0">
              {drafts.length === 0
                ? t<string>("workflow.editor.canvas.empty")
                : t<string>("workflow.editor.canvas.end")}
            </div>
          </div>
          </div>

          {/* View toolbar — fixed to the canvas, outside the transformed world. */}
          <div className="absolute top-2 right-2 z-10 flex items-center gap-0.5 rounded-lg border border-default-200 bg-content1/85 backdrop-blur px-1 py-0.5">
            <Button
              isIconOnly
              size="sm"
              title={t<string>("workflow.editor.canvas.zoomOut")}
              variant="light"
              onPress={canvasView.zoomOut}
            >
              <AiOutlineMinus />
            </Button>
            <Button
              className="min-w-12 px-1 text-xs text-default-500"
              size="sm"
              title={t<string>("workflow.editor.canvas.resetZoom")}
              variant="light"
              onPress={canvasView.resetZoom}
            >
              {canvasView.zoomPct}%
            </Button>
            <Button
              isIconOnly
              size="sm"
              title={t<string>("workflow.editor.canvas.zoomIn")}
              variant="light"
              onPress={canvasView.zoomIn}
            >
              <AiOutlinePlus />
            </Button>
            <Button
              isIconOnly
              size="sm"
              title={t<string>("workflow.editor.canvas.fit")}
              variant="light"
              onPress={canvasView.fit}
            >
              <AiOutlineExpand />
            </Button>
          </div>

          {/* Remove zone — appears while dragging an existing node; pinned to the canvas. */}
          <div
            className={`absolute bottom-2 left-1/2 -translate-x-1/2 z-10 rounded-lg border-1.5 border-dashed border-danger
              px-5 py-1 text-xs text-danger transition-opacity
              ${drag.dragging && drag.dragging.fromIdx != null ? "opacity-100" : "opacity-0 pointer-events-none"}
              ${drag.overRemove ? "bg-danger/20" : "bg-danger/5"}`}
            data-remove-zone
          >
            {t<string>("workflow.editor.canvas.removeZone")}
          </div>
        </div>

        {/* Inspector */}
        <div className="border-l border-default-200 max-md:border-l-0 max-md:border-t p-3 min-h-0 overflow-y-auto">
          <InspectorPanel
            drafts={drafts}
            filter={filter}
            selection={selection}
            triggerKind={triggerKind}
            triggerLocked={isEditing}
            triggers={supportedTriggers}
            onDraftChange={(idx, next) =>
              setDrafts((arr) => arr.map((a, i) => (i === idx ? next : a)))
            }
            onFilterChange={setFilter}
            onTriggerKindChange={onTriggerKindChange}
          />
        </div>
      </div>

      {workflow && (
        <WorkflowRunsDrawer
          isOpen={runsOpen}
          triggerKind={workflow.triggerKind}
          workflowDefinitionId={workflow.id}
          workflowName={name}
          onClose={() => setRunsOpen(false)}
        />
      )}
    </div>
  );
};

/** One insertion point. Zero width until the magnetic search activates it. */
const Slot: React.FC<{ index: number; active: boolean }> = ({ index, active }) => (
  <div
    className={`shrink-0 flex items-center justify-center overflow-visible transition-all duration-150
      ${active ? "w-[112px]" : "w-2"}`}
    data-slot={index}
  >
    <div
      className={`h-[84px] rounded-xl border-2 border-dashed transition-all duration-150
        ${active ? "w-[100px] border-primary bg-primary/10 shadow-[0_0_14px] shadow-primary/40" : "w-0 border-transparent"}`}
    />
  </div>
);

WorkflowCanvasEditor.displayName = "WorkflowCanvasEditor";

export default WorkflowCanvasEditor;
