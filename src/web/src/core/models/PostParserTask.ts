import type { PostParseTarget, PostParserSource } from "@/sdk/constants";

export interface PostParseTargetResult {
  data?: any;
  parsedAt?: string;
  error?: string;
}

export interface PostParserTask {
  id: number;
  source: PostParserSource;
  link: string;
  title?: string;
  content?: string;
  targets: PostParseTarget[];
  results?: Record<number, PostParseTargetResult>;
}
