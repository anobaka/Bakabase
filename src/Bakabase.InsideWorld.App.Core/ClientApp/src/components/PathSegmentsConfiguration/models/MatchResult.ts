import { MatchResultType } from '@/sdk/constants';

class MatchResult {
  type: MatchResultType;
  layer?: number;
  index?: number;
  matches?: string[];

  constructor(init?: Partial<MatchResult>) {
    Object.assign(this, init);
  }

  static OfLayer(layer: number, index?: number): MatchResult {
    return new MatchResult({
      type: MatchResultType.Layer,
      layer,
      index,
    });
  }

  static OfRegex(matches: string[]): MatchResult {
    return new MatchResult({
      type: MatchResultType.Regex,
      matches,
    });
  }
}

export {
  MatchResultType,
  MatchResult,
};
