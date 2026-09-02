import type { FsScanTarget } from "@/sdk/constants";

/**
 * Mirror of the backend FsManualScanPayload — for this trigger the definition's filter IS the
 * scan configuration, and a manual run builds its payload from it server-side.
 */
export interface FsManualScanFilter {
  roots: string[];
  target: FsScanTarget;
  /** 1 = direct children of each root. */
  depth: number;
  /** Extensions without dot; empty = all files. Applies to files only. */
  extensionFilter: string[];
}
