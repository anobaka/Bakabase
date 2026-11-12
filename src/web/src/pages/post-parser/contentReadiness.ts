import type {
  PostContentReadinessAssessment,
  PostContentSnapshot,
  PostParserTask,
} from "@/core/models/PostParserTask";

export interface ContentReadinessDecisionHistory {
  id: string;
  version: number;
  action: "confirm" | "revoke";
  contentFingerprint: string;
  sourceRevision: number;
  workflowRunId?: number | null;
  blockIds: string[];
  reason: string;
  createdAt: string;
}

/** UI-facing refinement of the generated contract's string-valued status and purpose fields. */
export interface ContentReadinessView {
  taskId: number;
  contentSnapshot?: PostContentSnapshot | null;
  source?: string | null;
  revision: number;
  workflowRunId?: number | null;
  contentFingerprint?: string | null;
  decisionVersion: number;
  status: "sufficient" | "needsUnlock" | "unknown";
  basis?: "unlocked" | "analysis" | "manual" | null;
  assessment?: PostContentReadinessAssessment | null;
  activeDecisionId?: string | null;
  hasStaleDecision: boolean;
  canConfirm: boolean;
  /** Only the server can identify a resumable content-unlock wait. */
  canContinue?: boolean;
  canRefresh?: boolean;
  parsedResultMayBeStale?: boolean;
  canRevoke: boolean;
  history: ContentReadinessDecisionHistory[];
}

export function remainingContentLocks(content?: PostContentSnapshot | null) {
  return content?.locks?.filter((lock) => !lock.isBought) ?? [];
}

/** The server projects only fingerprint-valid assessments. URLs never identify a content block. */
export function unresolvedContentLocks(
  content?: PostContentSnapshot | null,
  assessment = content?.readiness,
) {
  const decisions =
    assessment?.fingerprint && ["analysis", "manual"].includes(assessment.basis)
      ? assessment.decisions
      : [];
  return remainingContentLocks(content).filter((lock) => {
    if (!lock.blockId) return true;
    const matching = decisions.filter((decision) => decision.blockId === lock.blockId);
    return !matching.length || matching.some((decision) => decision.relevance !== "optional");
  });
}

export function hasBlockingContentLocks(task: PostParserTask): boolean {
  // IsComplete records historical extraction. A revocation restores the current lock requirements
  // without rewriting that history, so only the current snapshot's valid assessment can waive them.
  return unresolvedContentLocks(task.contentSnapshot).length > 0;
}
