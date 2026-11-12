"use client";

import { useSearchParams } from "react-router-dom";
import WorkflowBrowser from "@/components/Workflow/WorkflowBrowser";

/** The route owns URL state; all list, validation and editing behavior is shared with modal hosts. */
const WorkflowPage = () => {
  const [params, setParams] = useSearchParams();
  const triggerFilter = params.get("triggerKind")?.trim() || null;

  return (
    <WorkflowBrowser
      triggerFilter={triggerFilter}
      onTriggerFilterChange={(kind) => {
        const next = new URLSearchParams(params);
        if (kind) next.set("triggerKind", kind);
        else next.delete("triggerKind");
        setParams(next);
      }}
    />
  );
};

export default WorkflowPage;
