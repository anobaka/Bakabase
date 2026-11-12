export type SourceAvailabilityStatus =
  | "available"
  | "confirmedUnavailable"
  | "accessRequired"
  | "temporarilyUnavailable"
  | "unknown"
  | "unsupported";

/** UI projection of a dated, scoped observation; readiness is deliberately separate. */
export interface SourceHealth {
  status: string;
  code?: string | null;
  message?: string | null;
  checkedAt?: string | null;
  expiresAt?: string | null;
  evidence?: Record<string, string> | null;
  accountScope?: string | null;
}
export interface SourceAvailabilityRow {
  key: string;
  taskId: number;
  taskTitle?: string | null;
  partId: string;
  sourceId: string;
  url: string;
  originalUrl?: string | null;
  accessCode?: string | null;
  archivePassword?: string | null;
  providerId?: string | null;
  health?: SourceHealth | null;
  disabled: boolean;
  inUse: boolean;
  actionable: boolean;
  hasAlternative?: boolean;
  reasonCode?: string | null;
}
export interface SourceAvailabilityPreview {
  batchId: string;
  taskCount: number;
  sourceCount: number;
  expiresAt: string;
  status: "checking" | "ready" | "failed";
  processed: number;
  total: number;
  rows: SourceAvailabilityRow[];
}
export interface SourceAvailabilityResult {
  changedCount: number;
  skippedCount: number;
  rows?: { key: string; status: string; code?: string | null }[];
}
export type SourceAvailabilityAction = "disable" | "restore";
export type SourcePriorityOptions = { providerPriority: string[] };
export interface SourceProvider {
  id: string;
}

export function eligibleSource(
  row: SourceAvailabilityRow,
  action: SourceAvailabilityAction,
): boolean {
  if (!row.actionable || row.inUse) return false;
  return action === "restore"
    ? row.disabled
    : !row.disabled && row.health?.status === "confirmedUnavailable";
}

/** Unknown and unready methods remain visible and ordered, never labelled as invalid. */
export function orderProviders(catalog: string[], order: string[]): SourceProvider[] {
  const known = new Set(catalog);
  return [...new Set([...order, ...catalog])].filter((id) => known.has(id)).map((id) => ({ id }));
}
