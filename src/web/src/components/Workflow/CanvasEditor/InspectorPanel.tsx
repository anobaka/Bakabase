"use client";

import type { ActivityDraft, CanvasSelection } from "./types";
import type { components } from "@/sdk/BApi2";
import type { WorkflowDescription } from "../metadata";
import type { WorkflowActivityCategory } from "@/sdk/constants";

import React from "react";
import { useTranslation } from "react-i18next";

import { getWorkflowActivityUI } from "../Activities";
import { getWorkflowTriggerUI } from "../Triggers";
import { activityDisplayName, triggerDisplayName } from "../displayNames";
import { workflowDescription } from "../metadata";
import TriggerUsageSummary from "../TriggerUsageSummary";

import { CategoryTone } from "./CanvasNode";

import { HelpCenterButton } from "@/components/HelpCenter";
import { Select, Switch, Textarea } from "@/components/bakaui";
import { WorkflowActivityErrorBehavior } from "@/sdk/constants";

type TriggerDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];

interface Props {
  selection: CanvasSelection | null;
  // Trigger side
  triggers: TriggerDescriptorVm[];
  triggerKind: string;
  triggerLocked: boolean;
  filter: unknown;
  onTriggerKindChange: (kind: string) => void;
  onFilterChange: (filter: unknown) => void;
  // Activity side
  descriptors: (WorkflowDescription & { kind: string })[];
  drafts: ActivityDraft[];
  onDraftChange: (idx: number, next: ActivityDraft) => void;
}

/**
 * The fixed right-hand configuration column (design §2): the chain stays visible while a
 * node is configured, and nothing on the canvas ever jumps. The forms are the exact same
 * ConfigForm/FilterForm components every workflow surface uses — one implementation, no drift.
 */
const InspectorPanel: React.FC<Props> = ({
  selection,
  triggers,
  triggerKind,
  triggerLocked,
  filter,
  onTriggerKindChange,
  onFilterChange,
  drafts,
  descriptors,
  onDraftChange,
}) => {
  const { t } = useTranslation();

  if (selection == null) {
    return (
      <div className="text-sm text-default-400 text-center py-10">
        {t<string>("workflow.editor.inspector.empty")}
      </div>
    );
  }

  if (selection === "trigger") {
    const ui = getWorkflowTriggerUI(triggerKind);
    const FilterForm = ui?.FilterForm;
    const trigger = triggers.find((item) => item.kind === triggerKind);

    return (
      <div className="flex flex-col gap-3">
        <div className="text-[10px] tracking-wide text-secondary">
          {t<string>("workflow.editor.category.trigger")}
        </div>
        <TriggerUsageSummary compact trigger={trigger} triggerKind={triggerKind} />
        <Select
          dataSource={triggers
            .filter((tr) => !!getWorkflowTriggerUI(tr.kind))
            .map((tr) => ({
              value: tr.kind,
              label: triggerDisplayName(t, tr.kind, tr.displayName),
            }))}
          isDisabled={triggerLocked}
          label={t<string>("workflow.field.trigger")}
          selectedKeys={triggerKind ? [triggerKind] : []}
          size="sm"
          onSelectionChange={(keys) => {
            const next = Array.from(keys)[0] as string | undefined;

            if (next) onTriggerKindChange(next);
          }}
        />
        {triggerLocked && (
          <div className="text-[10.5px] text-default-400">
            {t<string>("workflow.editor.trigger.locked")}
          </div>
        )}
        {ui && FilterForm && filter != null && (
          <FilterForm value={filter} onChange={onFilterChange} />
        )}
      </div>
    );
  }

  const draft = drafts[selection];

  if (!draft) return null;
  const ui = getWorkflowActivityUI(draft.kind);
  const description = workflowDescription(
    descriptors.find((item) => item.kind === draft.kind) ?? {},
    t,
  );
  const tone =
    ui?.category != null ? CategoryTone[ui.category as WorkflowActivityCategory] : undefined;

  return (
    <div className="flex flex-col gap-3">
      <div>
        <div className={`text-[10px] tracking-wide ${tone?.text ?? "text-default-400"}`}>
          {tone ? t<string>(tone.labelKey) : draft.kind}
        </div>
        <div className="text-sm font-semibold">{activityDisplayName(t, draft.kind)}</div>
      </div>

      {description && <p className="text-xs leading-relaxed text-default-500">{description}</p>}
      <Textarea
        description={t<string>("workflow.field.nodeNotesDescription")}
        label={t<string>("workflow.field.nodeNotes")}
        minRows={2}
        value={draft.notes ?? ""}
        onValueChange={(notes) => onDraftChange(selection, { ...draft, notes })}
      />

      {ui ? (
        <ui.ConfigForm
          upstream={drafts
            .slice(0, selection)
            .map((d) => ({ kind: d.kind, configJson: d.configJson }))}
          value={ui.parseConfig(draft.configJson)}
          onChange={(next) =>
            onDraftChange(selection, { ...draft, configJson: ui.serializeConfig(next) })
          }
        />
      ) : (
        <p className="text-xs text-default-500">
          {t<string>("workflow.activity.unknownKind.noEditor")}
        </p>
      )}

      <div className="border-t border-default-200 pt-2 flex flex-col gap-2">
        <Switch
          isSelected={draft.onItemError === WorkflowActivityErrorBehavior.Skip}
          size="sm"
          onValueChange={(skip) =>
            onDraftChange(selection, {
              ...draft,
              onItemError: skip
                ? WorkflowActivityErrorBehavior.Skip
                : WorkflowActivityErrorBehavior.Fail,
            })
          }
        >
          <span className="text-xs">{t<string>("workflow.activity.skipOnItemError")}</span>
        </Switch>
        <HelpCenterButton label={t<string>("workflow.editor.inspector.help")} topic="workflow" />
      </div>
    </div>
  );
};

InspectorPanel.displayName = "InspectorPanel";

export default InspectorPanel;
