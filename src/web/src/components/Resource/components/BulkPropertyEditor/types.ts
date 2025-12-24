import type { PropertyPool } from "@/sdk/constants";

export type AggregatedValueState = "consistent" | "mixed" | "empty" | "partial";

export type PropertyKey = `${PropertyPool}-${number}`;

export type AggregatedPropertyValue = {
  state: AggregatedValueState;
  // If consistent, the shared value
  dbValue?: string;
  bizValue?: string;
  // Original values per resource for change tracking
  originalValues: Map<number, { dbValue?: string; bizValue?: string }>;
};

export const makePropertyKey = (pool: PropertyPool, id: number): PropertyKey => `${pool}-${id}`;
