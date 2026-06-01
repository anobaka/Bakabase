"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";
import type { ActivityDraft } from "./ActivityCard";
import type { DescriptorWithFit } from "./activityFit";
import type { WorkflowItemTypeDescriptorVm } from "./itemTypeRegistry";

import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { PlusCircleOutlined } from "@ant-design/icons";
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";

import ActivityCard from "./ActivityCard";
import ActivityPicker from "./ActivityPicker";
import ItemTypePill from "./ItemTypePill";
import { getWorkflowActivityUI } from "./Activities";
import { getWorkflowTriggerUI } from "./Triggers";
import { triggerDisplayName } from "./displayNames";
import { walkChain } from "./chainWalk";
import { classifyActivity } from "./activityFit";
import { WorkflowItemTypeIndex } from "./itemTypeRegistry";

import BApi from "@/sdk/BApi";
import { Button, Input, Modal, Select, Spinner, Switch, toast } from "@/components/bakaui";
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

/**
 * AI transform's kind is referenced in two places:
 *  - hidden from the picker (users never add it manually now)
 *  - auto-inserted in front of a bridge activity, with TargetItemType pinned to that
 *    activity's single accepted type
 *
 * Kept as a constant so a rename doesn't silently leave the editor broken.
 */
const AI_TRANSFORM_KIND = "transform.ai.transform";

interface Props extends DestroyableProps {
  workflow?: WorkflowVm;
  triggers: TriggerDescriptorVm[];
  onSaved?: () => void;
}

function activityVmToDraft(a: ActivityVm): ActivityDraft {
  return {
    clientId: crypto.randomUUID(),
    kind: a.kind,
    configJson: a.configJson,
    onItemError: a.onItemError as WorkflowActivityErrorBehavior,
  };
}

const WorkflowEditor: React.FC<Props> = ({
  workflow,
  triggers,
  onSaved,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const isEditing = !!workflow;

  const supportedTriggers = useMemo(
    () => triggers.filter((tr) => !!getWorkflowTriggerUI(tr.kind)),
    [triggers],
  );
  const initialTrigger = workflow?.triggerKind ?? supportedTriggers[0]?.kind ?? "";

  const [name, setName] = useState(workflow?.name ?? "");
  const [triggerKind, setTriggerKind] = useState(initialTrigger);
  const [enabled, setEnabled] = useState(workflow?.enabled ?? true);

  const triggerUi = useMemo(() => getWorkflowTriggerUI(triggerKind), [triggerKind]);

  const [filter, setFilter] = useState<any>(() => {
    if (!triggerUi) return null;
    return workflow?.triggerFilterJson
      ? triggerUi.parseFilter(workflow.triggerFilterJson)
      : triggerUi.defaultFilter();
  });

  const [activityDrafts, setActivityDrafts] = useState<ActivityDraft[]>(
    () => workflow?.activities?.map(activityVmToDraft) ?? [],
  );

  // Fetch ALL activity descriptors + item-type descriptors once. Both feed the chain walk
  // and the picker / type pills; static enough that we don't reload them after save.
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
      setItemTypes(new WorkflowItemTypeIndex((typeRsp.data ?? []) as WorkflowItemTypeDescriptorVm[]));
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const descriptorByKind = useMemo(
    () => new Map((allDescriptors ?? []).map((a) => [a.kind, a] as const)),
    [allDescriptors],
  );
  const activityDisplayNameByKind = useMemo(
    () => new Map((allDescriptors ?? []).map((a) => [a.kind, a.displayName] as const)),
    [allDescriptors],
  );

  const startItemType = useMemo(
    () => (triggerUi && filter !== null ? triggerUi.resolveOutputItemType(filter) : null),
    [triggerUi, filter],
  );
  const chain = useMemo(
    () =>
      startItemType === null
        ? null
        : walkChain(
            startItemType,
            activityDrafts.map((a) => ({ kind: a.kind, configJson: a.configJson })),
            descriptorByKind,
          ),
    [startItemType, activityDrafts, descriptorByKind],
  );

  // Activities offered in the picker. AI transform is hidden — users add destination
  // activities directly and we auto-insert a bridge when needed. Each remaining descriptor
  // is classified as "direct" (accepts current type) or "bridge" (single accepted type,
  // reachable via an AI transform). Multi-accept or unreachable activities are dropped.
  const addableWithFit = useMemo<DescriptorWithFit[]>(() => {
    if (!allDescriptors || chain === null) return [];
    const aiAvailable = !!getWorkflowActivityUI(AI_TRANSFORM_KIND)
      && !!allDescriptors.find((d) => d.kind === AI_TRANSFORM_KIND);
    const currentType = chain.typeAfter;
    return allDescriptors
      .filter((d) => d.kind !== AI_TRANSFORM_KIND)
      .filter((d) => !!getWorkflowActivityUI(d.kind))
      .map((d) => classifyActivity(d, currentType))
      .filter((x): x is DescriptorWithFit =>
        x !== null && (x.fit === "direct" || aiAvailable))
      // Suppress bridge variants when their target type equals the current type — that's
      // really a direct fit being misclassified by the "single accepted" heuristic.
      .filter((x) => !(x.fit === "bridge" && x.bridgeTargetType === chain.typeAfter));
  }, [allDescriptors, chain]);

  const onTriggerKindChange = (next: string) => {
    setTriggerKind(next);
    const ui = getWorkflowTriggerUI(next);
    setFilter(ui ? ui.defaultFilter() : null);
    setActivityDrafts([]);
  };

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const updateActivity = (idx: number, next: ActivityDraft) => {
    setActivityDrafts((arr) => arr.map((a, i) => (i === idx ? next : a)));
  };
  const onDragEnd = (event: { active: { id: any }; over: { id: any } | null }) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    setActivityDrafts((arr) => {
      const from = arr.findIndex((a) => a.clientId === active.id);
      const to = arr.findIndex((a) => a.clientId === over.id);
      if (from < 0 || to < 0) return arr;
      return arrayMove(arr, from, to);
    });
  };
  const deleteActivity = (idx: number) => {
    setActivityDrafts((arr) => arr.filter((_, i) => i !== idx));
  };
  const addActivity = (entry: DescriptorWithFit) => {
    const ui = getWorkflowActivityUI(entry.descriptor.kind);
    const defaultCfg = ui ? ui.serializeConfig(ui.defaultConfig()) : "{}";
    setActivityDrafts((arr) => {
      const next = [...arr];
      // For bridge fits, insert an AI transform with its TargetItemType pinned to the new
      // activity's only accepted type. The chain walk + runner then know exactly what shape
      // the LLM has to produce — without forcing the user to configure the AI node directly.
      if (entry.fit === "bridge" && entry.bridgeTargetType) {
        const aiUi = getWorkflowActivityUI(AI_TRANSFORM_KIND);
        if (aiUi) {
          const aiCfg = aiUi.serializeConfig({
            ...(aiUi.defaultConfig() as any),
            targetItemType: entry.bridgeTargetType,
          } as any);
          next.push({
            clientId: crypto.randomUUID(),
            kind: AI_TRANSFORM_KIND,
            configJson: aiCfg,
            onItemError: WorkflowActivityErrorBehavior.Fail,
          });
        }
      }
      next.push({
        clientId: crypto.randomUUID(),
        kind: entry.descriptor.kind,
        configJson: defaultCfg,
        onItemError: WorkflowActivityErrorBehavior.Fail,
      });
      return next;
    });
  };

  const openPicker = () => {
    createPortal(ActivityPicker, {
      addable: addableWithFit,
      onPick: (entry: DescriptorWithFit) => addActivity(entry),
    });
  };

  const isNameValid = name.trim().length > 0;
  const isTriggerValid = triggerKind.length > 0 && !!triggerUi;
  const isActivityConfigsValid = activityDrafts.every((a) => {
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

  const handleSave = async () => {
    if (!isValid || !triggerUi) return;
    const triggerFilterJson = triggerUi.serializeFilter(filter);
    const activitiesPayload = activityDrafts.map((a) => ({
      kind: a.kind,
      configJson: a.configJson,
      onItemError: a.onItemError,
    }));

    try {
      if (isEditing) {
        await BApi.workflow.patchWorkflow(workflow!.id, {
          name,
          triggerFilterJson,
          enabled,
          activities: activitiesPayload,
        });
      } else {
        await BApi.workflow.addWorkflow({
          name,
          triggerKind,
          triggerFilterJson,
          enabled,
          activities: activitiesPayload,
        });
      }
      onSaved?.();
    } catch (e: any) {
      toast.danger(e?.message ?? String(e));
      throw e;
    }
  };

  const FilterFormComponent = triggerUi?.FilterForm;

  return (
    <Modal
      defaultVisible
      okProps={{ isDisabled: !isValid }}
      size="3xl"
      title={isEditing ? t<string>("workflow.edit.title") : t<string>("workflow.create.title")}
      onDestroyed={onDestroyed}
      onOk={handleSave}
    >
      <div className="flex flex-col gap-4">
        <Input
          isRequired
          errorMessage={!isNameValid ? t<string>("workflow.validation.nameRequired") : undefined}
          isInvalid={!isNameValid}
          label={t<string>("workflow.field.name")}
          value={name}
          onValueChange={setName}
        />

        <Select
          isRequired
          dataSource={supportedTriggers.map((tr) => ({
            value: tr.kind,
            label: triggerDisplayName(t, tr.kind, tr.displayName),
          }))}
          isDisabled={isEditing}
          label={t<string>("workflow.field.trigger")}
          selectedKeys={triggerKind ? [triggerKind] : []}
          onSelectionChange={(keys) => {
            const next = Array.from(keys)[0] as string | undefined;
            if (next) onTriggerKindChange(next);
          }}
        />

        {triggerUi && FilterFormComponent && filter !== null && (
          <div className="border border-default-200 rounded-lg p-3 flex flex-col gap-2">
            <div className="text-sm font-medium">
              {t<string>("workflow.field.triggerFilter")}
            </div>
            <FilterFormComponent value={filter} onChange={setFilter} />
          </div>
        )}

        {/* Activities */}
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-2">
            <div className="text-sm font-medium">
              {t<string>("workflow.field.activities")}
            </div>
            {allDescriptors === null ? (
              <Spinner size="sm" />
            ) : (
              <Button
                color="primary"
                isDisabled={!triggerUi}
                size="sm"
                startContent={<PlusCircleOutlined />}
                variant="flat"
                onPress={openPicker}
              >
                {t<string>("workflow.activity.add")}
              </Button>
            )}
          </div>

          {/* Trigger output type pill (always shown when a trigger is picked). */}
          {itemTypes && startItemType && (
            <ItemTypePill fromTrigger index={itemTypes} itemType={startItemType} />
          )}

          {activityDrafts.length === 0 ? (
            <div className="text-xs text-default-500 px-1">
              {t<string>("workflow.activity.empty")}
            </div>
          ) : (
            <DndContext
              collisionDetection={closestCenter}
              sensors={sensors}
              onDragEnd={onDragEnd}
            >
              <SortableContext
                items={activityDrafts.map((a) => a.clientId)}
                strategy={verticalListSortingStrategy}
              >
                <div className="flex flex-col gap-2">
                  {activityDrafts.map((a, i) => (
                    <React.Fragment key={a.clientId}>
                      <ActivityCard
                        displayNameFallback={activityDisplayNameByKind.get(a.kind)}
                        draft={a}
                        incompatible={chain ? !chain.steps[i]?.compatible : false}
                        onChange={(next) => updateActivity(i, next)}
                        onDelete={() => deleteActivity(i)}
                      />
                      {/* Between cards (and after the last card): show the type leaving this
                          step, so users can read the chain's data shape at every position. */}
                      {itemTypes && chain && (
                        <ItemTypePill
                          index={itemTypes}
                          invalid={
                            i + 1 < activityDrafts.length && !chain.steps[i + 1]?.compatible
                          }
                          itemType={chain.steps[i].typeAfter}
                        />
                      )}
                    </React.Fragment>
                  ))}
                </div>
              </SortableContext>
            </DndContext>
          )}
        </div>

        <Switch isSelected={enabled} onValueChange={setEnabled}>
          {t<string>("workflow.field.enabled")}
        </Switch>
      </div>
    </Modal>
  );
};

export default WorkflowEditor;
