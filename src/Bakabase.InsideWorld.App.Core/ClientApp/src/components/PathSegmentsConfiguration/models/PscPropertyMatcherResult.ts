import type { PscMatchResult } from './PscMatchResult';
import type PscProperty from './PscProperty';

export type IPscPropertyMatcherResult = {
  property: PscProperty;
  results?: (PscMatchResult | undefined)[];
};
