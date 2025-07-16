import type { PscMatcherValue } from "./PscMatcherValue";
import type PscProperty from "./PscProperty";

export type IPscPropertyMatcherValue = {
  property: PscProperty;
  value: PscMatcherValue;
};
