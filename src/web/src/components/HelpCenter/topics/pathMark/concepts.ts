export interface PathMarkConcept {
  id: string;
  /** Show an extra example line (`helpCenter.pathMark.concept.{id}.example`). */
  hasExample?: boolean;
}

/**
 * Glossary of path mark concepts. Text lives under
 * `helpCenter.pathMark.concept.{id}.name/.short/.long[/.example]` so the same
 * wording can be reused by inline tips later without drifting.
 */
export const pathMarkConcepts: PathMarkConcept[] = [
  { id: "layer", hasExample: true },
  { id: "regex", hasExample: true },
  { id: "matchMode" },
  { id: "applyScope", hasExample: true },
  { id: "dynamicValue", hasExample: true },
  { id: "priority" },
  { id: "sync" },
  { id: "boundary" },
  { id: "scheduledSync" },
  { id: "keepIdentity" },
];
