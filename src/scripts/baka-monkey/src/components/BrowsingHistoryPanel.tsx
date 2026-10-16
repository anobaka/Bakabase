import { useEffect, useRef, useState } from 'react';
import {
  fetchDailyHistory,
  fetchPageBoundaries,
  formatBrowsingDistance,
  getPageDay,
  includePagePriorViews,
  shiftHistoryDay,
  type BrowsingPage,
  type DailyHistory,
  type PageBoundaries,
  type PageBoundary,
} from '../browsingHistory';
import type { ConnectionState } from '../heartbeat';
import { getApiAddressGeneration } from '../api';
import { describeRequestError, getLocale, onLocaleChange, t } from '../i18n';
import { RequestError } from '../requests';
import { getEffectiveTimeZone, onTimeZoneChange } from '../timezone';

interface HistoryRequest<T> {
  key: string;
  data: T | null;
  error: unknown;
}

const chartWidth = 160;
const chartHeight = 28;

function formatPublicationTime(iso: string, precision: 'day' | 'minute'): string {
  const date = new Date(iso);
  if (!Number.isFinite(date.getTime())) return iso;
  try {
    if (precision === 'day') {
      return date.toLocaleDateString(getLocale() === 'zh' ? 'zh-CN' : 'en', {
        timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
      });
    }
    return date.toLocaleString(getLocale() === 'zh' ? 'zh-CN' : 'en', {
      timeZone: getEffectiveTimeZone(),
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', timeZoneName: 'short',
    });
  } catch {
    return date.toISOString();
  }
}

export function BrowsingHistoryPanel({ siteKey, page, connection, showOlder, showNewer, showChart }: {
  siteKey: string;
  page: BrowsingPage;
  connection: ConnectionState;
  showOlder: boolean;
  showNewer: boolean;
  showChart: boolean;
}) {
  const [, forceUpdate] = useState(0);
  const [boundariesRequest, setBoundariesRequest] = useState<HistoryRequest<PageBoundaries> | null>(null);
  const [dailyRequest, setDailyRequest] = useState<HistoryRequest<DailyHistory> | null>(null);
  const [chartWindow, setChartWindow] = useState<{ scope: string; day: string } | null>(null);
  const chartRef = useRef<HTMLDivElement>(null);
  const lastWheelAtRef = useRef(0);
  const hasDates = !page.hasMissingDates && page.oldestUpdatedAt !== null && page.newestUpdatedAt !== null &&
    Number.isFinite(Date.parse(page.oldestUpdatedAt)) && Number.isFinite(Date.parse(page.newestUpdatedAt));
  const timeZoneId = page.timePrecision === 'day' ? 'Asia/Shanghai' : getEffectiveTimeZone();
  // Counts change after history queries and this visit's marks; neither moves the chart window.
  const pageScope = JSON.stringify([
    siteKey, page.filter, page.contentIds, page.contentDates, page.oldestUpdatedAt, page.newestUpdatedAt,
    page.chartCenterUpdatedAt, page.hasMissingDates, timeZoneId,
  ]);
  const initialCenterDay = hasDates ? getPageDay(page.chartCenterUpdatedAt ?? page.oldestUpdatedAt!, timeZoneId) : '';
  const pageDays = new Set(page.contentDates.map((date) => getPageDay(date, timeZoneId)));
  const centerDay = chartWindow?.scope === pageScope ? chartWindow.day : initialCenterDay;
  const boundaryKey = JSON.stringify([pageScope, connection.connected, connection.baseUrl, getApiAddressGeneration(), showOlder || showNewer]);
  const dailyKey = JSON.stringify([pageScope, connection.connected, connection.baseUrl, getApiAddressGeneration(), showChart, centerDay]);
  const boundaries = boundariesRequest?.key === boundaryKey ? boundariesRequest : null;
  const daily = dailyRequest?.key === dailyKey ? dailyRequest : null;

  useEffect(() => onLocaleChange(() => forceUpdate((value) => value + 1)), []);
  useEffect(() => onTimeZoneChange(() => forceUpdate((value) => value + 1)), []);

  useEffect(() => {
    setBoundariesRequest(null);
    if (!(showOlder || showNewer) || !connection.connected || !hasDates || !page.contentIds.length) return;
    let cancelled = false;
    let cancelRequest: (() => void) | undefined;
    try {
      cancelRequest = fetchPageBoundaries({
        page, domainKey: siteKey, filter: page.filter,
        onUpdate: (data) => { if (!cancelled) setBoundariesRequest({ key: boundaryKey, data, error: null }); },
        onError: (error) => { if (!cancelled) setBoundariesRequest({ key: boundaryKey, data: null, error }); },
      });
    } catch (error) {
      setBoundariesRequest({ key: boundaryKey, data: null, error });
    }
    return () => { cancelled = true; cancelRequest?.(); };
  }, [boundaryKey]);

  useEffect(() => {
    setDailyRequest(null);
    if (!showChart || !connection.connected || !hasDates || !page.contentIds.length) return;
    let cancelled = false;
    let cancelRequest: (() => void) | undefined;
    try {
      cancelRequest = fetchDailyHistory({
        page, domainKey: siteKey, filter: page.filter, centerDay, timeZoneId,
        onUpdate: (data) => { if (!cancelled) setDailyRequest({ key: dailyKey, data, error: null }); },
        onError: (error) => { if (!cancelled) setDailyRequest({ key: dailyKey, data: null, error }); },
      });
    } catch (error) {
      setDailyRequest({ key: dailyKey, data: null, error });
    }
    return () => { cancelled = true; cancelRequest?.(); };
  }, [dailyKey]);

  const moveWindow = (offset: number) => setChartWindow({ scope: pageScope, day: shiftHistoryDay(centerDay, offset) });
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) return;
    const onWheel = (event: WheelEvent) => {
      const delta = event.deltaY || event.deltaX;
      if (!delta) return;
      event.preventDefault();
      event.stopPropagation();
      // A trackpad gesture emits many events; move once until it pauses.
      const previousWheelAt = lastWheelAtRef.current;
      lastWheelAtRef.current = Date.now();
      if (lastWheelAtRef.current - previousWheelAt < 150) return;
      setChartWindow({ scope: pageScope, day: shiftHistoryDay(centerDay, delta > 0 ? 7 : -7) });
    };
    chart.addEventListener('wheel', onWheel, { passive: false });
    return () => chart.removeEventListener('wheel', onWheel);
  }, [showChart, hasDates, connection.connected, centerDay, pageScope]);

  if (!(showChart || showOlder || showNewer) || !page.contentIds.length) return null;

  const description = t(page.timePrecision === 'day' ? 'browsingHistory.dayDescription' : 'browsingHistory.description');
  const chartDescription = t(page.timePrecision === 'day' ? 'browsingHistory.dayChartDescription' : 'browsingHistory.chartDescription');
  const renderFailure = (error: unknown) => {
    const updateServer = error instanceof RequestError && (error.status === 404 || error.status === 405);
    const detail = error ? describeRequestError(error) : t('disconnected');
    return (
      <div title={`${description}\n${detail}`} style={{ color: '#71717a' }}>
        {t(updateServer ? 'browsingHistory.updateServer' : 'browsingHistory.unavailable')}
      </div>
    );
  };
  const renderBoundary = (side: 'older' | 'newer', edge: string, boundary: PageBoundary | null | undefined) => {
    const gap = boundary ? (side === 'older'
      ? formatBrowsingDistance(boundary.updatedAt, edge, page.timePrecision)
      : formatBrowsingDistance(edge, boundary.updatedAt, page.timePrecision)) : null;
    const value = !boundaries?.data ? t('browsingHistory.loading') :
      boundary ? gap ?? t('browsingHistory.unavailable') :
        t(side === 'older' ? 'browsingHistory.noOlder' : 'browsingHistory.noNewer');
    const tooltip = [description, t('browsingHistory.pageEdge', { time: formatPublicationTime(edge, page.timePrecision) }),
      boundary ? t('browsingHistory.neighbor', {
        id: boundary.contentId, time: formatPublicationTime(boundary.updatedAt, page.timePrecision),
      }) : value].join('\n');
    return (
      <div key={side} title={tooltip} style={{ display: 'flex', alignItems: 'baseline', gap: 4 }}>
        <span aria-hidden="true">{side === 'older' ? '↓' : '↑'}</span>
        <span>{t(side === 'older' ? 'browsingHistory.older' : 'browsingHistory.newer')}</span>
        <strong style={{ fontVariantNumeric: 'tabular-nums', overflowWrap: 'anywhere' }}>{value}</strong>
      </div>
    );
  };
  const chartDays = daily?.data ? includePagePriorViews(daily.data, page, timeZoneId).days : [];
  const maximum = Math.max(1, ...chartDays.map((day) => day.count));
  const dateRange = centerDay ? `${shiftHistoryDay(centerDay, -15)} – ${shiftHistoryDay(centerDay, 15)}` : '';
  const compactDateRange = centerDay ? `${shiftHistoryDay(centerDay, -15).slice(5)} – ${shiftHistoryDay(centerDay, 15).slice(5)}` : '';
  const buttonStyle = { padding: 0, border: 0, background: 'transparent', color: '#71717a', fontSize: 10, lineHeight: '18px', cursor: 'pointer' } as const;

  return (
    <div
      data-browsing-history={siteKey}
      role="group"
      aria-label={description}
      style={{
        display: 'flex', flexDirection: 'column', gap: 2,
        width: 'fit-content', maxWidth: 'min(220px, calc(100vw - 40px))',
        padding: '4px 6px', borderRadius: 8,
        background: '#fff', border: '1px solid #e4e4e7',
        boxShadow: '0 4px 20px rgba(0,0,0,0.12)',
        color: '#27272a', fontSize: 11, lineHeight: '16px',
        fontFamily: 'system-ui, -apple-system, "Segoe UI", sans-serif',
      }}
    >
      {page.priorViewedCount > 0 && (
        <div title={description}>{t('browsingHistory.priorViewed', { count: page.priorViewedCount })}</div>
      )}
      {page.priorStatusPending && (
        <div role="status" style={{ color: '#71717a' }}>{t('browsingHistory.checkingPage')}</div>
      )}
      {!hasDates ? (
        <div title={description} style={{ color: '#71717a' }}>{t('browsingHistory.unavailableDates')}</div>
      ) : !connection.connected ? renderFailure(connection.error) : (
        <>
          {showChart && (
            <div style={{ width: chartWidth, maxWidth: '100%' }}>
              <div
                ref={chartRef}
                role="group"
                tabIndex={0}
                aria-label={`${t('browsingHistory.chartLabel')}: ${dateRange}. ${chartDescription}`}
                title={chartDescription}
                onKeyDown={(event) => {
                  if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
                  event.preventDefault();
                  event.stopPropagation();
                  moveWindow(event.key === 'ArrowRight' ? 7 : -7);
                }}
                style={{ display: 'flex', alignItems: 'flex-end', gap: 1, height: chartHeight, width: '100%' }}
              >
                {daily?.error ? renderFailure(daily.error) : !daily?.data ? (
                  <span role="status" style={{ color: '#71717a' }}>{t('browsingHistory.loading')}</span>
                ) : chartDays.map(({ date, count }) => {
                  const withinPage = pageDays.has(date);
                  const title = `${t('browsingHistory.chartDay', { date, count })}\n${t(withinPage ? 'browsingHistory.currentPage' : 'browsingHistory.noCurrentPageItems')}`;
                  return (
                    <div
                      key={date}
                      role="img"
                      title={title}
                      aria-label={title}
                      style={{
                        display: 'flex', alignItems: 'flex-end', flex: 1, minWidth: 0, height: chartHeight,
                      }}
                    >
                      <div style={{ width: '100%', height: count ? Math.max(2, Math.round(count / maximum * chartHeight)) : 1, background: withinPage ? '#eab308' : count ? '#17a34a' : '#d4d4d8' }} />
                    </div>
                  );
                })}
              </div>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2, marginTop: 1 }}>
                <button type="button" aria-label={t('browsingHistory.previous')} title={t('browsingHistory.previous')} onClick={() => moveWindow(-7)} style={{ ...buttonStyle, width: 18, height: 18 }}>‹</button>
                <button type="button" title={`${dateRange}\n${t('browsingHistory.reset')}`} aria-label={`${dateRange}. ${t('browsingHistory.reset')}`} onClick={() => setChartWindow(null)} style={buttonStyle}>{compactDateRange}</button>
                <button type="button" aria-label={t('browsingHistory.next')} title={t('browsingHistory.next')} onClick={() => moveWindow(7)} style={{ ...buttonStyle, width: 18, height: 18 }}>›</button>
              </div>
            </div>
          )}
          {(showOlder || showNewer) && (boundaries?.error ? renderFailure(boundaries.error) : (
            <>
              {showOlder && renderBoundary('older', page.oldestUpdatedAt!, boundaries?.data?.older)}
              {showNewer && renderBoundary('newer', page.newestUpdatedAt!, boundaries?.data?.newer)}
            </>
          ))}
        </>
      )}
    </div>
  );
}
