import type { BTaskResourceType, BTaskStatus, BTaskType } from "@/sdk/constants";

export type BTask = {
  id: string;
  name: string;
  description?: string;
  percentage?: number;
  process?: string;
  interval?: string; // ISO 8601 duration string (e.g., "PT2H") for TimeSpan equivalent
  enableAfter?: string; // ISO 8601 datetime string for DateTime equivalent
  status: BTaskStatus; // Assume BTaskStatus is defined elsewhere as an enum or type
  error?: string;
  briefError?: string;
  messageOnInterruption?: string;
  conflictWithTaskKeys?: string[];
  estimateRemainingTime?: string; // ISO 8601 duration string
  startedAt?: string; // ISO 8601 datetime string
  createdAt: string; // ISO 8601 datetime string
  reasonForUnableToStart?: string;
  nextTimeStartAt?: string;
  isPersistent: boolean;
  elapsed?: string;
  type: BTaskType;
  resourceType: BTaskResourceType;
  resourceKeys?: (string | number)[];
  data?: unknown;
};
