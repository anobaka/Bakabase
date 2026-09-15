import type { WorkflowTriggerDescriptor } from "./triggerPresentation";

import { useTranslation } from "react-i18next";

import { getTriggerActivationMode, useWorkflowTriggerDescriptors } from "./triggerPresentation";

import { Chip } from "@/components/bakaui";

export interface WorkflowTriggerBadgeProps {
  trigger?: WorkflowTriggerDescriptor;
  triggerKind?: string;
}

const WorkflowTriggerBadge = ({ trigger, triggerKind }: WorkflowTriggerBadgeProps) => {
  const { t } = useTranslation();
  const { data } = useWorkflowTriggerDescriptors(!trigger && !!triggerKind);
  const mode = getTriggerActivationMode(trigger ?? data?.find((item) => item.kind === triggerKind));
  const color =
    mode === "manual"
      ? "default"
      : mode === "module"
        ? "primary"
        : mode === "systemEvent"
          ? "secondary"
          : mode === "unknown"
            ? "default"
            : "success";

  return (
    <Chip color={color} size="sm" variant="flat">
      {t(`workflowTriggers.mode.${mode}`)}
    </Chip>
  );
};

export default WorkflowTriggerBadge;
