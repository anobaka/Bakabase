export interface BangumiSubjectRelationsTarget {
  /** A subject id, or the page URL it was copied from. */
  subject: string;
  /** Which relations to keep, in Bangumi's own Chinese labels. Empty keeps all of them. */
  relations: string[];
  /** Whether the subject itself is a member, not only what it is related to. */
  includeSelf: boolean;
}

/** The relations Bangumi publishes, as it labels them. */
export const BANGUMI_RELATIONS = [
  "续集",
  "前传",
  "系列",
  "外传",
  "衍生",
  "不同版本",
  "主线故事",
  "书籍",
  "游戏",
  "动画",
] as const;
