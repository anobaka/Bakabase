import type { BTaskStatus } from '@/sdk/constants';

export type BTask = {
  id: string;
  name: string;
  description?: string;
  percentage?: number;
  interval?: string; // ISO 8601 duration string (e.g., "PT2H") for TimeSpan equivalent
  enableAfter?: string; // ISO 8601 datetime string for DateTime equivalent
  status: BTaskStatus; // Assume BTaskStatus is defined elsewhere as an enum or type
  error?: string;
  messageOnInterruption?: string;
  conflictWithTaskKeys?: Set<string>;
  estimateRemainingTime?: string; // ISO 8601 duration string
  startedAt?: string; // ISO 8601 datetime string
  reasonForUnableToStart?: string;
  isPersistent: boolean;
};
