"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Button, Chip, Modal, Spinner, Textarea, toast } from "@/components/bakaui";
import { getWorkflowTriggerUI } from "@/components/Workflow/Triggers";
import InvocationParameterFields from "./Parameters/InvocationParameterFields";
import { parameterValueIsValid, type WorkflowParameter } from "./Parameters/model";

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
    draft[f.name] =
      f.type === "string" ? "" : f.type.endsWith("[]") ? [] : f.type === "bool" ? false : 0;
  }

  return JSON.stringify(draft, null, 2);
}

const ManualRunModal = ({ workflowId, workflowName, trigger, onRan }: Props) => {
  const { t } = useTranslation();
  const triggerUI = getWorkflowTriggerUI(trigger.kind);
  const ManualRunForm = triggerUI?.ManualRunForm;
  const skeleton = useMemo(
    () => triggerUI?.defaultManualPayload?.() ?? buildSkeleton(trigger),
    [trigger, triggerUI],
  );
  const [argsJson, setArgsJson] = useState<string>(skeleton);
  const [parameters, setParameters] = useState<WorkflowParameter[]>();
  const [loadFailed, setLoadFailed] = useState(false);
  const [reload, setReload] = useState(0);
  const [values, setValues] = useState<Record<string, unknown>>({});
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  useEffect(() => {
    let active = true;
    setParameters(undefined);
    setLoadFailed(false);
    void BApi.workflow
      .getWorkflow(workflowId)
      .then((response) => {
        if (!active) return;
        if (response.code || !response.data) throw new Error("Unable to load workflow");
        setParameters(response.data.parameters ?? []);
      })
      .catch(() => {
        if (active) setLoadFailed(true);
      });
    return () => {
      active = false;
    };
  }, [workflowId, reload]);
  const reportValidity = useCallback(
    (key: string, valid: boolean) =>
      setInvalid((current) => {
        if (current.has(key) === !valid) return current;
        const next = new Set(current);
        if (valid) next.delete(key);
        else next.add(key);
        return next;
      }),
    [],
  );
  const parametersValid =
    !!parameters &&
    !invalid.size &&
    parameters.every((parameter) =>
      parameterValueIsValid(
        parameter,
        Object.prototype.hasOwnProperty.call(values, parameter.key)
          ? values[parameter.key]
          : parameter.defaultValue,
      ),
    );

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
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled:
            trigger.supportsManualRun === false ||
            !parametersValid ||
            !!parseError ||
            triggerUI?.isManualPayloadValid?.(argsJson) === false,
        },
      }}
      size="lg"
      title={t<string>("workflow.manualRun.title", { name: workflowName })}
      onOk={async () => {
        if (trigger.supportsManualRun === false)
          throw new Error(t<string>("workflow.entry.managed"));
        if (!parametersValid) throw new Error(t<string>("workflow.parameters.invalid"));
        // Thrown so the modal stays open on a rejected payload — the server validates the shape,
        // not just the syntax, and the user needs the box they typed into to still be there.
        const rsp = await BApi.workflow.runWorkflowManually(workflowId, {
          argsJson,
          parameterValues: values,
        });

        if (rsp.code) throw new Error(rsp.message);
        toast.success(t<string>("workflow.manualRun.started"));
        onRan?.();
      }}
    >
      <div className="flex flex-col gap-2">
        {trigger.requiresManualPayload &&
          (ManualRunForm ? (
            <ManualRunForm value={argsJson} onChange={setArgsJson} />
          ) : (
            <>
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
            </>
          ))}
        {parseError && <div className="text-xs text-danger">{parseError}</div>}
        {loadFailed ? (
          <div role="alert" className="space-y-2 text-sm text-danger">
            <p>{t("workflow.parameters.loadFailed")}</p>
            <Button size="sm" variant="flat" onPress={() => setReload((value) => value + 1)}>
              {t("common.action.retry")}
            </Button>
          </div>
        ) : !parameters ? (
          <div className="flex justify-center py-6">
            <Spinner />
          </div>
        ) : (
          <InvocationParameterFields
            parameters={parameters}
            values={values}
            onChange={setValues}
            onValidityChange={reportValidity}
          />
        )}
      </div>
    </Modal>
  );
};

ManualRunModal.displayName = "ManualRunModal";

export default ManualRunModal;
