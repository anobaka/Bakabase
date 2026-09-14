import type { WorkflowDescription, WorkflowValidation } from "./metadata";

import React from "react";
import { useTranslation } from "react-i18next";

import { workflowDescription } from "./metadata";
import { activityDisplayName } from "./displayNames";
import WorkflowDiagnostics from "./WorkflowDiagnostics";

type Props = {
  workflow: WorkflowDescription & { validation?: WorkflowValidation | null };
  activityKinds: string[];
  showDiagnostics?: boolean;
};

const WorkflowSummary = ({ workflow, activityKinds, showDiagnostics = true }: Props) => {
  const { t } = useTranslation();
  const description = workflowDescription(workflow, t);

  return (
    <div className="flex flex-col gap-3 text-xs">
      <p className="whitespace-pre-wrap break-words leading-relaxed text-default-600">
        {description || t<string>("workflow.description.empty")}
      </p>
      <ol className="flex flex-wrap gap-1.5 text-default-500">
        {activityKinds.map((kind, index) => (
          <li key={`${index}-${kind}`} className="rounded-md bg-default-100 px-2 py-1">
            {index + 1}. {activityDisplayName(t, kind)}
          </li>
        ))}
      </ol>
      {showDiagnostics && <WorkflowDiagnostics result={workflow.validation} />}
    </div>
  );
};

export default WorkflowSummary;
