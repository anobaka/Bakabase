import type { PostParseTarget, PostParserSource, WorkflowRunStatus } from "@/sdk/constants";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionHandoffView } from "@/sdk/Api";

export interface PostAvailability {
  status: "noExpiryReported" | "expired" | "restored" | "unknown";
  evidence: string[];
  reason?: string | null;
}

export interface PostContentReadinessAssessment {
  basis: "analysis" | "manual";
  fingerprint: string;
  policyVersion: string;
  decisions: {
    blockId: string;
    relevance: "required" | "optional" | "unknown";
    purpose:
      | "purchaseProof"
      | "donation"
      | "unrelated"
      | "supplement"
      | "downloadCredentials"
      | "processingInstructions"
      | "unknown";
    evidence: string[];
    reason?: string | null;
  }[];
}

export interface PostContentSnapshot {
  currencyCode?: string | null;
  currencyName?: string | null;
  readiness?: PostContentReadinessAssessment | null;
  title?: string;
  mainHtml?: string;
  commentHtmlList?: string[];
  comments?: { id?: string; floor?: string; author?: string; postedAt?: string; html: string }[];
  sourceUrl?: string;
  capturedAt?: string;
  scope?: string;
  balance?: number | null;
  locks: {
    blockId?: string | null;
    contextText?: string | null;
    currencyCode?: string | null;
    currencyName?: string | null;
    priceText?: string | null;
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
  downloadMedia?: {
    coverCandidates?: { url: string; kind: string; evidence?: string[] }[];
    excludedResourceIndices?: number[];
  } | null;
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
  attentionReasonCodes?: string[];
  minimumRemainingCoins?: number;
  autoBuyThreshold?: number;
  purchaseQuote?: {
    currencyCode?: string | null;
    currencyName?: string | null;
    eligibleTotalText?: string;
    excludedTotalText?: string;
    eligibleLockUrls: string[];
    eligibleTotal: number;
    excludedTotal: number;
    excludedCount: number;
    unknownPriceCount: number;
  } | null;
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
  acquisitionHandoff?: BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionHandoffView | null;
}
