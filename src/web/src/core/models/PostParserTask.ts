import type { PostParseTarget, PostParserSource } from "@/sdk/constants";

export interface PostParserTask {
  id: number;
  source: PostParserSource;
  link: string;
  title?: string;
  content?: string;
  targets: PostParseTarget[];
  results?: Record<number, any>;
  error?: string;
  isDeleted?: boolean;
}
