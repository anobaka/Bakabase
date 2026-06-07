import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { SiteConfig, ContentStatus } from './types';
import { getApiBaseUrl, httpRequest } from './api';
import { SettingsPanel } from './components/SettingsPanel';
import { ParseTaskButton } from './actions/ParseTaskButton';
import { DownloadTaskButton } from './actions/DownloadTaskButton';
import { ContentTrackerBadge } from './actions/ContentTrackerBadge';
import { startHeartbeat, isConnected, onConnectionChange } from './heartbeat';
import { t } from './i18n';

interface MarkerEntry {
  id: string;
  element: HTMLElement;
  container: HTMLElement;
  status: ContentStatus;
  /** ISO timestamp when this item was previously downloaded, or null. */
  downloadedAt: string | null;
  /** Whether this content was already viewed *before* the current page load. */
  visitedBefore: boolean;
}

const DEFAULT_STATUS: ContentStatus = {
  isViewed: false,
  hasUpdate: false,
  viewedAt: null,
  updatedAt: null,
};

// Tag content elements the user has visited before so index.css can draw a
// frame around them. We mutate host elements directly (not via the React
// portal) because the frame belongs to the host's own content card.
function applyVisitedHighlight(element: HTMLElement, visitedBefore: boolean, hasUpdate: boolean) {
  element.classList.toggle('bk-visited', visitedBefore);
  element.classList.toggle('bk-has-update', visitedBefore && hasUpdate);
}

function isElementInViewport(element: HTMLElement): boolean {
  const rect = element.getBoundingClientRect();
  return (
    rect.top >= 0 &&
    rect.left >= 0 &&
    rect.bottom <= (window.innerHeight || document.documentElement.clientHeight) &&
    rect.right <= (window.innerWidth || document.documentElement.clientWidth)
  );
}

export function App({ siteConfigs }: { siteConfigs: SiteConfig[] }) {
  const [markers, setMarkers] = useState<MarkerEntry[]>([]);
  const [connected, setConnected] = useState(isConnected);
  const statusMapRef = useRef(new Map<string, ContentStatus>());
  // downloadKey (== adapter.extractUrl) -> ISO download time, or null when queried but never downloaded.
  const downloadRecordsRef = useRef(new Map<string, string | null>());
  // Ids that were already viewed when first queried this page load. Marking an
  // item viewed during the current visit does NOT add it here, so the "visited
  // before" frame only appears on revisits — the first visit stays untouched.
  const visitedBeforeRef = useRef(new Set<string>());
  const siteConfig = useMemo(() => {
    const hostname = window.location.hostname;
    return siteConfigs.find((c) => c.domains.some((d) => hostname.includes(d))) ?? null;
  }, [siteConfigs]);

  // Start heartbeat and track connection state
  useEffect(() => {
    startHeartbeat();
    return onConnectionChange(setConnected);
  }, []);

  const scanAndRender = useCallback(() => {
    if (!siteConfig) return;

    const elements = siteConfig.findContents(document);
    const entries: MarkerEntry[] = [];

    for (const element of elements) {
      const info = siteConfig.extractContentInfo(element);
      if (!info.id) continue;

      // Create or reuse a container for the React portal
      // Check element itself and its parent (some sites append to a wrapper element)
      let container = element.querySelector<HTMLElement>('.bk-marker')
        ?? element.parentElement?.querySelector<HTMLElement>(`:scope > .bk-marker`);
      if (!container) {
        container = siteConfig.createContainer(element);
      }

      const status = statusMapRef.current.get(info.id) ?? DEFAULT_STATUS;

      let downloadedAt: string | null = null;
      if (siteConfig.downloadTask) {
        const dlKey = siteConfig.downloadTask.extractUrl(element);
        if (dlKey) downloadedAt = downloadRecordsRef.current.get(dlKey) ?? null;
      }

      const visitedBefore = visitedBeforeRef.current.has(info.id);
      entries.push({ id: info.id, element, container, status, downloadedAt, visitedBefore });
    }

    setMarkers(entries);
  }, [siteConfig]);

  const queryDownloadRecords = useCallback((keys: string[]) => {
    if (!siteConfig?.downloadTask || keys.length === 0) return;

    const { thirdPartyId } = siteConfig.downloadTask;
    httpRequest({
      method: 'POST',
      url: `${getApiBaseUrl()}/download-task/records/query`,
      data: { thirdPartyId, keys },
      onSuccess: (result: any) => {
        // Mark every queried key as resolved so it is not re-queried on the next scroll.
        for (const k of keys) {
          if (!downloadRecordsRef.current.has(k)) downloadRecordsRef.current.set(k, null);
        }
        if (result.data) {
          for (const item of result.data) {
            if (item.key) downloadRecordsRef.current.set(item.key, item.downloadedAt ?? null);
          }
        }
        scanAndRender();
      },
    });
  }, [siteConfig, scanAndRender]);

  const queryStatus = useCallback((contentIds: string[]) => {
    if (!siteConfig || contentIds.length === 0) return;

    const filter = siteConfig.extractFilter(window.location.href);

    httpRequest({
      method: 'POST',
      url: `${getApiBaseUrl()}/third-party-content-tracker/query`,
      data: { domainKey: siteConfig.key, filter, contentIds },
      onSuccess: (result: any) => {
        if (!result.data) return;
        for (const item of result.data) {
          statusMapRef.current.set(item.contentId, {
            isViewed: item.isViewed,
            hasUpdate: item.hasUpdate,
            viewedAt: item.viewedAt ? new Date(item.viewedAt) : null,
            updatedAt: item.updatedAt ? new Date(item.updatedAt) : null,
          });
          // Snapshot the "already viewed" state at query time, before any
          // mark-viewed this session flips it.
          if (item.isViewed) visitedBeforeRef.current.add(item.contentId);
        }
        scanAndRender();
      },
    });
  }, [siteConfig, scanAndRender]);

  const collectNewDownloadKeys = useCallback((elements: HTMLElement[]): string[] => {
    const dl = siteConfig?.downloadTask;
    if (!dl) return [];
    const keys: string[] = [];
    for (const el of elements) {
      const key = dl.extractUrl(el);
      if (key && !downloadRecordsRef.current.has(key) && !keys.includes(key)) {
        keys.push(key);
      }
    }
    return keys;
  }, [siteConfig]);

  // After a successful download click, optimistically mark the item as downloaded so the
  // button turns warning + tooltip updates immediately, without waiting for a re-query.
  const markDownloaded = useCallback((element: HTMLElement) => {
    const dl = siteConfig?.downloadTask;
    if (!dl) return;
    const key = dl.extractUrl(element);
    if (!key) return;
    downloadRecordsRef.current.set(key, new Date().toISOString());
    scanAndRender();
  }, [siteConfig, scanAndRender]);

  const markVisibleAsViewed = useCallback(() => {
    if (!siteConfig) return;

    const elements = siteConfig.findContents(document);
    const visibleUnviewed: Array<{ contentId: string; updatedAt: Date | null }> = [];

    for (const element of elements) {
      const info = siteConfig.extractContentInfo(element);
      if (!info.id) continue;

      const status = statusMapRef.current.get(info.id);
      if (status?.isViewed) continue;
      if (!isElementInViewport(element)) continue;

      visibleUnviewed.push({ contentId: info.id, updatedAt: info.updateTime });
    }

    if (visibleUnviewed.length === 0) return;

    const filter = siteConfig.extractFilter(window.location.href);
    httpRequest({
      method: 'POST',
      url: `${getApiBaseUrl()}/third-party-content-tracker/mark-viewed`,
      data: {
        domainKey: siteConfig.key,
        filter,
        contentItems: visibleUnviewed.map((i) => ({
          contentId: i.contentId,
          updatedAt: i.updatedAt?.toISOString() ?? null,
        })),
      },
      onSuccess: () => {
        for (const item of visibleUnviewed) {
          const existing = statusMapRef.current.get(item.contentId);
          if (existing) {
            existing.isViewed = true;
            existing.viewedAt = new Date();
            existing.hasUpdate = false;
            if (item.updatedAt) existing.updatedAt = item.updatedAt;
          } else {
            statusMapRef.current.set(item.contentId, {
              isViewed: true,
              hasUpdate: false,
              viewedAt: new Date(),
              updatedAt: item.updatedAt,
            });
          }
        }
        scanAndRender();
      },
    });
  }, [siteConfig, scanAndRender]);

  // Initial scan + scroll listener
  useEffect(() => {
    if (!siteConfig) return;

    console.log(t('siteDetected', { site: siteConfig.key }));

    // Initial scan
    const elements = siteConfig.findContents(document);
    const newIds: string[] = [];
    for (const el of elements) {
      const info = siteConfig.extractContentInfo(el);
      if (info.id && !statusMapRef.current.has(info.id)) {
        newIds.push(info.id);
      }
    }
    scanAndRender();
    if (newIds.length > 0) queryStatus(newIds);
    const newDlKeys = collectNewDownloadKeys(elements);
    if (newDlKeys.length > 0) queryDownloadRecords(newDlKeys);

    // Scroll-based content discovery
    let scrollTimeout: ReturnType<typeof setTimeout>;
    const handleScroll = () => {
      clearTimeout(scrollTimeout);
      scrollTimeout = setTimeout(() => {
        const els = siteConfig.findContents(document);
        const ids: string[] = [];
        for (const el of els) {
          const info = siteConfig.extractContentInfo(el);
          if (info.id && !statusMapRef.current.has(info.id)) {
            ids.push(info.id);
          }
        }
        scanAndRender();
        if (ids.length > 0) queryStatus(ids);
        const dlKeys = collectNewDownloadKeys(els);
        if (dlKeys.length > 0) queryDownloadRecords(dlKeys);
      }, 300);
    };
    window.addEventListener('scroll', handleScroll);

    // Site-specific mark-viewed setup
    siteConfig.onMarkViewed(markVisibleAsViewed);

    return () => {
      window.removeEventListener('scroll', handleScroll);
      clearTimeout(scrollTimeout);
    };
  }, [siteConfig, scanAndRender, queryStatus, markVisibleAsViewed, collectNewDownloadKeys, queryDownloadRecords]);

  // Frame the content cards the user has visited before.
  useEffect(() => {
    for (const m of markers) {
      applyVisitedHighlight(m.element, m.visitedBefore, m.status.hasUpdate);
    }
  }, [markers]);

  if (!siteConfig && !__DEV__) return null;

  return (
    <>
      <SettingsPanel siteKey={siteConfig?.key} connected={connected} />
      {(connected || __DEV__) && siteConfig && markers.map((m) =>
        createPortal(
          <div style={{ pointerEvents: 'auto', display: 'inline-flex', gap: 4, alignItems: 'center' }}>
            {/* Shared action buttons rendered from adapter declarations */}
            <ContentTrackerBadge status={m.status} />
            {siteConfig.parseTask && (
              <ParseTaskButton
                adapter={siteConfig.parseTask}
                postUrl={siteConfig.parseTask.extractPostUrl(m.element)}
              />
            )}
            {siteConfig.downloadTask && (
              <DownloadTaskButton
                adapter={siteConfig.downloadTask}
                element={m.element}
                downloadedAt={m.downloadedAt}
                onDownloaded={() => markDownloaded(m.element)}
              />
            )}
            {/* Site-specific custom marker (overlays, etc.) */}
            {siteConfig.renderMarker?.(m.element, m.status)}
          </div>,
          m.container,
        ),
      )}
    </>
  );
}
