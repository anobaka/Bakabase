export interface SoulPlusSearchTarget {
  /** A board's list page, as pasted from the browser. */
  url: string;
  /** Only threads whose title contains one of these. Empty watches the whole board. */
  keywords: string[];
  /** How many list pages to walk per check. */
  maxPages: number;
}
