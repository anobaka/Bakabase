import { getApiAddressGeneration, getApiBaseUrl, httpRequest } from './api';

export interface TaskSummaryTarget {
  kind: 'download' | 'parse';
  source: number;
}

export interface TaskSummary {
  completed: number;
  failed: number;
  total: number;
}

interface SummaryResponse {
  code?: number;
  data?: TaskSummary;
}

export function getTaskPageUrl(baseUrl: string, target: TaskSummaryTarget): string {
  return `${baseUrl}/#/${target.kind === 'download' ? 'downloader' : 'post-parser'}`;
}

function readSummary(response: SummaryResponse): TaskSummary | null {
  const summary = response.data;
  if (response.code !== 0 || !summary) return null;
  const { completed, failed, total } = summary;
  if (![completed, failed, total].every((value) => Number.isSafeInteger(value) && value >= 0)
    || completed + failed > total) return null;
  return { completed, failed, total };
}

/** One request at a time; stopping or changing server invalidates pending answers. */
export function startTaskSummaryPolling({ target, onUpdate }: {
  target: TaskSummaryTarget;
  onUpdate: (summary: TaskSummary | null) => void;
}): () => void {
  const baseUrl = getApiBaseUrl();
  const generation = getApiAddressGeneration();
  const path = target.kind === 'download'
    ? `/download-task/summary?thirdPartyId=${target.source}`
    : `/post-parser/task/summary?source=${target.source}`;
  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const current = () => !stopped && baseUrl === getApiBaseUrl()
    && generation === getApiAddressGeneration();

  function finish(summary: TaskSummary | null) {
    if (!current()) return;
    onUpdate(summary);
    if (current()) timer = setTimeout(poll, 10_000);
  }

  function poll() {
    if (!current()) return;
    httpRequest<SummaryResponse>({
      method: 'GET',
      url: `${baseUrl}${path}`,
      timeout: 8_000,
      onSuccess: (response) => finish(readSummary(response)),
      onError: () => finish(null),
    });
  }

  onUpdate(null);
  if (baseUrl) poll();
  return () => {
    stopped = true;
    clearTimeout(timer);
  };
}
