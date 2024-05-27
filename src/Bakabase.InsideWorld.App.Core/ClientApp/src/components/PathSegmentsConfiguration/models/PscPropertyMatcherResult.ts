import type { PscMatchResult } from './PscMatchResult';
import type PscProperty from './PscProperty';
import type { IPscPropertyMatcherValue } from '@/components/PathSegmentsConfiguration/models/PscPropertyMatcherValue';

export type IPscPropertyMatcherResult = {
  pmv: IPscPropertyMatcherValue;
  result?: PscMatchResult;
  indexByProperty?: number;
};
