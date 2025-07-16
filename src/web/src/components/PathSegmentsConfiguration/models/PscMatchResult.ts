import { MatchResultType } from "@/sdk/constants";

class PscMatchResult {
  type: MatchResultType;
  layer?: number;
  index?: number;
  matches?: string[];

  constructor(init?: Partial<PscMatchResult>) {
    Object.assign(this, init);
  }

  static OfLayer(layer: number, index?: number): PscMatchResult {
    return new PscMatchResult({
      type: MatchResultType.Layer,
      layer,
      index,
    });
  }

  static OfRegex(matches: string[]): PscMatchResult {
    return new PscMatchResult({
      type: MatchResultType.Regex,
      matches,
    });
  }
}

export { PscMatchResult };
