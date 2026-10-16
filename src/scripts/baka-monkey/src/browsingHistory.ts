import { getApiAddressGeneration, getApiBaseUrl, httpRequest } from './api';
import { t } from './i18n';
import { RequestError } from './requests';
import type { ContentInfo, ContentStatus } from './types';

export interface BrowsingPage {
  filter: string | null;
  timePrecision: 'day' | 'minute';
  contentIds: string[];
  oldestUpdatedAt: string | null;
  newestUpdatedAt: string | null;
  chartCenterUpdatedAt: string | null;
  contentDates: string[];
  priorViewedCount: number;
  priorViewedItems: Array<{ contentId: string; updatedAt: string }>;
  priorStatusPending: boolean;
  hasMissingDates: boolean;
}

export interface PageBoundary {
  contentId: string;
  updatedAt: string;
}

export interface PageBoundaries {
  older: PageBoundary | null;
  newer: PageBoundary | null;
}

export interface DailyHistory {
  days: Array<{ date: string; count: number }>;
}

export function validContentDate(value: Date | null): Date | null {
  return value && Number.isFinite(value.getTime()) ? value : null;
}

/** Use the whole page's time limits, irrespective of its visual sort order. */
export function buildBrowsingPage(contents: ContentInfo[], priorViewStates: Map<string, boolean>, filter: string | null = null,
  timePrecision: 'day' | 'minute' = 'minute'): BrowsingPage {
  const items = new Map(contents.filter((item) => item.id).map((item) => [item.id!, item]));
  const contentIds = [...items.keys()];
  const dates = [...items.values()].map((item) => validContentDate(item.updateTime)?.getTime() ?? null);
  const hasMissingDates = dates.some((date) => date === null);
  const times = dates.filter((date): date is number => date !== null).sort((a, b) => a - b);
  return {
    filter,
    timePrecision,
    contentIds,
    oldestUpdatedAt: !hasMissingDates && times.length ? new Date(Math.min(...times)).toISOString() : null,
    newestUpdatedAt: !hasMissingDates && times.length ? new Date(Math.max(...times)).toISOString() : null,
    chartCenterUpdatedAt: !hasMissingDates && times.length ? new Date(times[Math.floor(times.length / 2)]).toISOString() : null,
    contentDates: times.map((time) => new Date(time).toISOString()),
    priorViewedCount: contentIds.filter((id) => priorViewStates.get(id) === true).length,
    priorViewedItems: [...items.values()].filter((item) => priorViewStates.get(item.id!) === true && validContentDate(item.updateTime))
      .map((item) => ({ contentId: item.id!, updatedAt: item.updateTime!.toISOString() })),
    priorStatusPending: contentIds.some((id) => !priorViewStates.has(id)),
    hasMissingDates,
  };
}

export function getPageDay(iso: string, timeZoneId: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: timeZoneId, year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(new Date(iso));
  const part = (type: string) => parts.find((value) => value.type === type)?.value;
  return `${part('year')}-${part('month')}-${part('day')}`;
}

/** Add the frozen prior views on this page, without counting newly auto-marked items. */
export function includePagePriorViews(history: DailyHistory, page: BrowsingPage, timeZoneId: string): DailyHistory {
  const counts = new Map<string, number>();
  for (const item of page.priorViewedItems) {
    const day = getPageDay(item.updatedAt, timeZoneId);
    counts.set(day, (counts.get(day) ?? 0) + 1);
  }
  return { days: history.days.map(({ date, count }) => ({ date, count: count + (counts.get(date) ?? 0) })) };
}

/** Calendar arithmetic in UTC avoids browser-local DST changing the chart's dates. */
export function shiftHistoryDay(day: string, offset: number): string {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + offset);
  return date.toISOString().slice(0, 10);
}

export function fetchDailyHistory({ page, domainKey, filter, centerDay, timeZoneId, onUpdate, onError }: {
  page: BrowsingPage;
  domainKey: string;
  filter: string | null;
  centerDay: string;
  timeZoneId: string;
  onUpdate: (history: DailyHistory) => void;
  onError: (error: RequestError) => void;
}): () => void {
  const baseUrl = getApiBaseUrl();
  const generation = getApiAddressGeneration();
  let stopped = false;
  const current = () => !stopped && baseUrl === getApiBaseUrl() && generation === getApiAddressGeneration();
  if (page.contentIds.length && !page.hasMissingDates && centerDay) {
    httpRequest<{ code?: number; data?: DailyHistory }>({
      method: 'POST', url: `${baseUrl}/third-party-content-tracker/daily-history`, timeout: 8_000,
      data: { domainKey, filter, centerDay, timeZoneId, excludeContentIds: page.contentIds },
      onSuccess: (response) => {
        if (!current()) return;
        const days = response.data?.days;
        if (response.code !== 0 || !Array.isArray(days) || days.length !== 31 || days.some((day, index) =>
          !day || day.date !== shiftHistoryDay(centerDay, index - 15)
          || !Number.isSafeInteger(day.count) || day.count < 0)) {
          onError(new RequestError('invalid-response'));
          return;
        }
        onUpdate({ days: days.map(({ date, count }) => ({ date, count })) });
      },
      onError: (error) => { if (current()) onError(error); },
    });
  }
  return () => { stopped = true; };
}

/** Freeze the first history answer; a late query after our own mark is not a revisit. */
export function applyQueriedContentStatus(
  statuses: Map<string, ContentStatus>, priorViewStates: Map<string, boolean>, id: string, status: ContentStatus,
): void {
  if (!priorViewStates.has(id)) priorViewStates.set(id, status.isViewed);
  // A query dispatched before mark-viewed may return after it. Keep that confirmed mark.
  if (!statuses.get(id)?.isViewed || status.isViewed) statuses.set(id, status);
}

export function canAutoMarkContent(status: ContentStatus | undefined, priorViewResolved: boolean, contentDate: Date | null = null): boolean {
  return priorViewResolved && !!status && (!status.isViewed || (!!contentDate && status.updatedAt === null));
}

function readBoundary(value: unknown): PageBoundary | null | undefined {
  if (value === null) return null;
  if (!value || typeof value !== 'object') return undefined;
  const boundary = value as Partial<PageBoundary>;
  if (typeof boundary.contentId !== 'string' || !boundary.contentId
    || typeof boundary.updatedAt !== 'string' || !boundary.updatedAt.endsWith('Z')
    || !Number.isFinite(Date.parse(boundary.updatedAt))) return undefined;
  return { contentId: boundary.contentId, updatedAt: boundary.updatedAt };
}

export function fetchPageBoundaries({ page, domainKey, filter, onUpdate, onError }: {
  page: BrowsingPage;
  domainKey: string;
  filter: string | null;
  onUpdate: (boundaries: PageBoundaries) => void;
  onError: (error: RequestError) => void;
}): () => void {
  const baseUrl = getApiBaseUrl();
  const generation = getApiAddressGeneration();
  let stopped = false;
  const current = () => !stopped && baseUrl === getApiBaseUrl() && generation === getApiAddressGeneration();
  if (page.contentIds.length && !page.hasMissingDates && page.oldestUpdatedAt && page.newestUpdatedAt) {
    httpRequest<{ code?: number; data?: { older?: unknown; newer?: unknown } }>({
      method: 'POST',
      url: `${baseUrl}/third-party-content-tracker/page-boundaries`,
      timeout: 8_000,
      data: { domainKey, filter, oldestUpdatedAt: page.oldestUpdatedAt, newestUpdatedAt: page.newestUpdatedAt, excludeContentIds: page.contentIds },
      onSuccess: (response) => {
        if (!current()) return;
        const older = readBoundary(response.data?.older);
        const newer = readBoundary(response.data?.newer);
        const ids = new Set(page.contentIds);
        if (response.code !== 0 || older === undefined || newer === undefined
          || (older && (ids.has(older.contentId) || Date.parse(older.updatedAt) > Date.parse(page.oldestUpdatedAt!)))
          || (newer && (ids.has(newer.contentId) || Date.parse(newer.updatedAt) < Date.parse(page.newestUpdatedAt!)))) {
          onError(new RequestError('invalid-response'));
          return;
        }
        onUpdate({ older, newer });
      },
      onError: (error) => { if (current()) onError(error); },
    });
  }
  return () => { stopped = true; };
}

/** Publication-time distance, not elapsed time since browsing or an unread count. */
export function formatBrowsingDistance(fromIso: string, toIso: string, precision: 'day' | 'minute' = 'minute'): string | null {
  const difference = Math.abs(Date.parse(fromIso) - Date.parse(toIso));
  if (!Number.isFinite(difference)) return null;
  if (precision === 'day') return t('browsingHistory.day', { count: Math.floor(difference / 86_400_000) });
  if (difference === 0) return t('browsingHistory.minute', { count: 0 });
  const minutes = Math.floor(difference / 60_000);
  if (!minutes) return t('browsingHistory.lessThanMinute');
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const remainingMinutes = minutes % 60;
  return [
    days ? t('browsingHistory.day', { count: days }) : '',
    hours ? t('browsingHistory.hour', { count: hours }) : '',
    remainingMinutes ? t('browsingHistory.minute', { count: remainingMinutes }) : '',
  ].filter(Boolean).join(' ');
}
