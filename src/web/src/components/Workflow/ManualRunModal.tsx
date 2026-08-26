"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Chip, Modal, Textarea, toast } from "@/components/bakaui";

type TriggerDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];

interface Props extends DestroyableProps {
  workflowId: number;
  workflowName: string;
  trigger: TriggerDescriptorVm;
  onRan?: () => void;
}

/**
 * A manual run of an event trigger is a replay: nothing happened, so the user has to supply
 * something for the event to have been. The payload's field list comes from the server so the
 * editor can start from a real skeleton rather than an empty box.
 */
function buildSkeleton(trigger: TriggerDescriptorVm): string {
  const draft: Record<string, unknown> = {};

  for (const f of trigger.payloadFields ?? []) {
    if (f.nullable) continue;
    draft[f.name] = f.type === "string" ? "" : f.type.endsWith("[]") ? [] : f.type === "bool" ? false : 0;
  }

  return JSON.stringify(draft, null, 2);
}

const ManualRunModal = ({ workflowId, workflowName, trigger, onRan }: Props) => {
  const { t } = useTranslation();
  const skeleton = useMemo(() => buildSkeleton(trigger), [trigger]);
  const [argsJson, setArgsJson] = useState<string>(skeleton);

  const parseError = useMemo(() => {
    if (!argsJson.trim()) return null;
    try {
      JSON.parse(argsJson);

      return null;
    } catch (e: any) {
      return e?.message ?? String(e);
    }
  }, [argsJson]);

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["ok", "cancel"], okProps: { isDisabled: !!parseError } }}
      size="lg"
      title={t<string>("workflow.manualRun.title", { name: workflowName })}
      onOk={async () => {
        // Thrown so the modal stays open on a rejected payload — the server validates the shape,
        // not just the syntax, and the user needs the box they typed into to still be there.
        const rsp = await BApi.workflow.runWorkflowManually(workflowId, { argsJson });

        if (rsp.code) throw new Error(rsp.message);
        toast.success(t<string>("workflow.manualRun.started"));
        onRan?.();
      }}
    >
      <div className="flex flex-col gap-2">
        <div className="text-sm text-default-500">{t<string>("workflow.manualRun.hint")}</div>
        {(trigger.payloadFields ?? []).length > 0 && (
          <div className="flex flex-wrap gap-1">
            {trigger.payloadFields.map((f) => (
              <Chip key={f.name} radius="sm" size="sm" variant="flat">
                {f.name}
                <span className="opacity-50">
                  : {f.type}
                  {f.nullable ? "?" : ""}
                </span>
              </Chip>
            ))}
          </div>
        )}
        <Textarea
          className="font-mono"
          minRows={8}
          value={argsJson}
          onValueChange={setArgsJson}
        />
        {parseError && <div className="text-xs text-danger">{parseError}</div>}
      </div>
    </Modal>
  );
};

ManualRunModal.displayName = "ManualRunModal";

export default ManualRunModal;
