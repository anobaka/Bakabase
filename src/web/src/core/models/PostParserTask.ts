import type { PostParseTarget, PostParserSource, WorkflowRunStatus } from "@/sdk/constants";

export interface PostParserTask {
  id: number;
  source: PostParserSource | 0;
  link: string;
  text?: string | null;
  title?: string;
  content?: string;
  targets: PostParseTarget[];
  results?: Record<string | number, unknown>;
  error?: string;
  isDeleted?: boolean;
  workflowRunId?: number | null;
  workflowDefinitionId?: number | null;
  workflowStatus?: WorkflowRunStatus | null;
  revision?: number;
}
