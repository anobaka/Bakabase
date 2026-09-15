export interface VndbSeriesTarget {
  /** A visual novel id (v17), or the page URL it was copied from. */
  visualNovel: string;
  /** Which relations to keep, in VNDB's own words. Empty keeps all of them. */
  relations: string[];
  /** Drop fan works and unofficial ports. */
  officialOnly: boolean;
  /** Whether the visual novel itself is a member, not only what it is related to. */
  includeSelf: boolean;
}

/**
 * The relation kinds VNDB publishes, with the words it uses for them. The codes are what the API
 * takes; the labels are what it shows on a page.
 */
export const VNDB_RELATIONS = [
  { value: "seq", label: "Sequel" },
  { value: "preq", label: "Prequel" },
  { value: "ser", label: "Same series" },
  { value: "set", label: "Same setting" },
  { value: "alt", label: "Alternative version" },
  { value: "char", label: "Shares characters" },
  { value: "side", label: "Side story" },
  { value: "par", label: "Parent story" },
  { value: "orig", label: "Original game" },
  { value: "fan", label: "Fandisc" },
] as const;
