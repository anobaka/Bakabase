import type { PostParseTarget, PostParserSource, WorkflowRunStatus } from "@/sdk/constants";

export interface PostAvailability {
  status: "noExpiryReported" | "expired" | "restored" | "unknown";
  evidence: string[];
  reason?: string | null;
}

export interface PostContentSnapshot {
  title?: string;
  mainHtml?: string;
  commentHtmlList?: string[];
  comments?: { id?: string; floor?: string; author?: string; postedAt?: string; html: string }[];
  sourceUrl?: string;
  capturedAt?: string;
  scope?: string;
  balance?: number | null;
  locks: {
    url?: string | null;
    price?: number | null;
    isBought: boolean;
    id?: string;
    floor?: string;
  }[];
}

export interface PostParserTask {
  id: number;
  source: PostParserSource | 0;
  link: string;
  text?: string | null;
  title?: string;
  content?: string;
  contentSnapshot?: PostContentSnapshot | null;
  availability?: PostAvailability | null;
  parsingState?:
    | "snapshotSaved"
    | "awaitingAi"
    | "awaitingPurchase"
    | "possiblyExpired"
    | "partial"
    | "complete"
    | null;
  parsingMessage?: string | null;
  minimumRemainingCoins?: number;
  autoBuyThreshold?: number;
  createdAt?: string | null;
  completedAt?: string | null;
  targets: PostParseTarget[];
  results?: Record<string | number, unknown>;
  error?: string;
  isDeleted?: boolean;
  workflowRunId?: number | null;
  workflowDefinitionId?: number | null;
  workflowStatus?: WorkflowRunStatus | null;
  revision?: number;
}
