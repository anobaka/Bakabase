import envConfig from "@/config/env";

export type AdaptiveMigrationRow = {
  taskId: number;
  title?: string | null;
  workflowRunId?: number | null;
  runStatus?: string | null;
  currentStepIndex?: number | null;
  canMigrate: boolean;
  alreadyEnabled: boolean;
  reasonCode: string;
  reason?: string | null;
};

export type AdaptiveMigrationPreview = {
  token: string;
  expiresAt: string;
  rows: AdaptiveMigrationRow[];
  canMigrateCount: number;
};

export type AdaptiveMigrationResult = {
  migratedTaskIds: number[];
  skipped: AdaptiveMigrationRow[];
};

async function post<T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${envConfig.apiEndpoint}${path}`, {
    method: "POST",
    signal,
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const payload = await response.json().catch(() => undefined);
  if (!response.ok || payload?.code) {
    throw new Error(payload?.message || `HTTP_${response.status}`);
  }
  if (payload?.data == null) throw new Error("ADAPTIVE_MIGRATION_EMPTY_RESPONSE");
  return payload.data as T;
}

export const acquisitionAdaptiveProcessingApi = {
  preview: (taskIds: number[], maxIterations = 3, signal?: AbortSignal) =>
    post<AdaptiveMigrationPreview>("/acquisition/adaptive-processing/preview", {
      taskIds,
      maxIterations,
    }, signal),
  apply: (previewToken: string, taskIds: number[]) =>
    post<AdaptiveMigrationResult>("/acquisition/adaptive-processing/apply", {
      previewToken,
      taskIds,
    }),
};
