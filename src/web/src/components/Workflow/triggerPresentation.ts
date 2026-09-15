import type { components } from "@/sdk/BApi2";
import type { TFunction } from "i18next";

import { QueryClient, useQuery } from "@tanstack/react-query";

import { workflowTriggerSources } from "./Triggers";

import BApi from "@/sdk/BApi";
import { WorkflowActivationMode } from "@/sdk/constants";

export type WorkflowTriggerDescriptor =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowTriggerDescriptorViewModel"];

// Shared by source pages, the editor and help dialogs; no per-row requests or global provider needed.
const triggerQueryClient = new QueryClient();

export function useWorkflowTriggerDescriptors(enabled = true) {
  return useQuery(
    {
      queryKey: ["workflow-trigger-descriptors", BApi.baseUrl],
      queryFn: async (): Promise<WorkflowTriggerDescriptor[]> => {
        const response = await BApi.workflow.getWorkflowTriggers();

        if (response.code !== 0 || !Array.isArray(response.data)) {
          throw new Error(response.message ?? "Unable to load workflow triggers");
        }

        return response.data;
      },
      enabled,
      staleTime: 60_000,
      retry: false,
    },
    triggerQueryClient,
  );
}

export const triggerActivationModes = [
  "unknown",
  "manual",
  "module",
  "systemEvent",
  "schedule",
  "watch",
] as const;
export type TriggerActivationMode = (typeof triggerActivationModes)[number];

/** SupportsManualRun describes an extra execution entry, never the trigger's activation mode. */
export function getTriggerActivationMode(
  trigger?: WorkflowTriggerDescriptor,
): TriggerActivationMode {
  switch (trigger?.activationMode) {
    case WorkflowActivationMode.Manual:
      return "manual";
    case WorkflowActivationMode.Module:
      return "module";
    case WorkflowActivationMode.SystemEvent:
      return "systemEvent";
    case WorkflowActivationMode.Schedule:
      return "schedule";
    case WorkflowActivationMode.Watch:
      return "watch";
    default:
      return "unknown";
  }
}

export function triggerSourceLabel(trigger: WorkflowTriggerDescriptor, t: TFunction): string {
  const source =
    workflowTriggerSources[trigger.sourceModule as keyof typeof workflowTriggerSources];

  return source
    ? t<string>(source.labelKey)
    : trigger.sourceModule || t<string>("workflowTriggers.source.unknown");
}
