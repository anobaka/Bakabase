export interface WorkflowConcept {
  id: string;
  /** Show an extra example line (`helpCenter.workflow.concept.{id}.example`). */
  hasExample?: boolean;
}

/**
 * Glossary of workflow concepts. Text lives under
 * `helpCenter.workflow.concept.{id}.name/.short/.long[/.example]` so the same
 * wording can be reused by inline tips later without drifting.
 */
export const workflowConcepts: WorkflowConcept[] = [
  { id: "trigger", hasExample: true },
  { id: "activity" },
  { id: "itemType", hasExample: true },
  { id: "chainTyping", hasExample: true },
  { id: "variables", hasExample: true },
  { id: "twoPhase", hasExample: true },
  { id: "automation", hasExample: true },
  { id: "manualRun" },
  { id: "errorPolicy", hasExample: true },
  { id: "runs" },
];
