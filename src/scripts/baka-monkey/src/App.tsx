import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { CoverOverlayConfig, SiteConfig, ContentStatus, DownloadTaskState } from './types';
import { getApiAddressGeneration, getApiBaseUrl, httpRequest } from './api';
import { SettingsPanel } from './components/SettingsPanel';
import { ParseTaskButton } from './actions/ParseTaskButton';
import { DownloadTaskButton } from './actions/DownloadTaskButton';
import { ContentTrackerBadge } from './actions/ContentTrackerBadge';
import { startHeartbeat, isConnected, getConnectionState, onConnectionChange } from './heartbeat';
import { isCoverOverlayEnabled, onSettingsChange } from './settings';
import { t } from './i18n';
import type { TaskSummaryTarget } from './taskSummary';
import { applyQueriedContentStatus, buildBrowsingPage, canAutoMarkContent, validContentDate } from './browsingHistory';

interface MarkerEntry {
  id: string;
  element: HTMLElement;
  container: HTMLElement;
  status: ContentStatus;
  /** Backend view of this item's download key, or null when there is nothing on record. */
  downloadState: DownloadTaskState | null;
  /** Portal host laid over the item's cover, or null when it has no cover. */
  coverContainer: HTMLElement | null;
  /** Whether this content was already viewed *before* the current page load. */
  visitedBefore: boolean;
  updateTime: Date | null;
}

const DEFAULT_STATUS: ContentStatus = {
  isViewed: false,
  hasUpdate: false,
  viewedAt: null,
  updatedAt: null,
};

const COVER_OVERLAY_HOST_CLASS = 'bk-cover-overlay-host';

// Tag content elements the user has visited before so index.css can draw a
// frame around them. We mutate host elements directly (not via the React
// portal) because the frame belongs to the host's own content card.
function applyVisitedHighlight(element: HTMLElement, visitedBefore: boolean, hasUpdate: boolean) {
  element.classList.toggle('bk-visited', visitedBefore);
  element.classList.toggle('bk-has-update', visitedBefore && hasUpdate);
}

// Park a portal host on top of the item's cover. It is inert by itself
// (pointer-events:none in index.css); only the overlay rendered into it catches
// clicks, so nothing changes for users who leave the feature off.
function ensureCoverContainer(element: HTMLElement, config: CoverOverlayConfig): HTMLElement | null {
  const cover = config.findCover(element);
  if (!cover) return null;

  const existing = cover.querySelector<HTMLElement>(`:scope > .${COVER_OVERLAY_HOST_CLASS}`);
  if (existing) return existing;

  const style = getComputedStyle(cover);
  // An inline anchor gives absolute children an unreliable containing block.
  // inline-block fixes that while still shrinking to the thumbnail, so the overlay
  // lands on the image rather than on the whole width of its container.
  if (style.display === 'inline') cover.style.display = 'inline-block';
  if (style.position === 'static') cover.style.position = 'relative';

  const host = document.createElement('div');
  host.className = COVER_OVERLAY_HOST_CLASS;
  cover.appendChild(host);
  return host;
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
  const [connection, setConnection] = useState(getConnectionState);
  const { connected } = connection;
  const [coverOverlayEnabled, setCoverOverlayEnabled] = useState(false);
  const statusMapRef = useRef(new Map<string, ContentStatus>());
  const statusBaseUrlRef = useRef(getApiBaseUrl());
  const statusGenerationRef = useRef(getApiAddressGeneration());
  const apiGeneration = getApiAddressGeneration();
  // downloadKey (== adapter.extractUrl) -> backend state, or null when the backend
  // knows nothing about it (queried, never downloaded, not queued).
  const downloadStatesRef = useRef(new Map<string, DownloadTaskState | null>());
  // Read inside scanAndRender, which must not change identity when the setting is
  // toggled: the scan effect below re-registers listeners whenever it does.
  const coverOverlayEnabledRef = useRef(false);
  // Ids that were already viewed when first queried this page load. Marking an
  // item viewed during the current visit does NOT add it here, so the "visited
  // before" frame only appears on revisits — the first visit stays untouched.
  const priorViewStatesRef = useRef(new Map<string, boolean>());
  const markingIdsRef = useRef(new Set<string>());
  const markVisibleRef = useRef(() => {});
  const siteConfig = useMemo(() => {
    const hostname = window.location.hostname;
    return siteConfigs.find((c) => c.domains.some((d) => hostname.includes(d))) ?? null;
  }, [siteConfigs]);
  const taskSummaryTarget = useMemo<TaskSummaryTarget | undefined>(() => {
    if (siteConfig?.downloadTask) return { kind: 'download', source: siteConfig.downloadTask.thirdPartyId };
    if (siteConfig?.parseTask) return { kind: 'parse', source: siteConfig.parseTask.source };
    return undefined;
  }, [siteConfig]);

  // Start heartbeat and track connection state
  useEffect(() => {
    startHeartbeat();
    return onConnectionChange(setConnection);
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

      let downloadState: DownloadTaskState | null = null;
      if (siteConfig.downloadTask) {
        const dlKey = siteConfig.downloadTask.extractUrl(element);
        if (dlKey) downloadState = downloadStatesRef.current.get(dlKey) ?? null;
      }

      const coverContainer = siteConfig.coverOverlay && coverOverlayEnabledRef.current
        ? ensureCoverContainer(element, siteConfig.coverOverlay)
        : null;

      const visitedBefore = priorViewStatesRef.current.get(info.id) === true;
      entries.push({ id: info.id, element, container, status, downloadState, coverContainer, visitedBefore, updateTime: validContentDate(info.updateTime) });
    }

    setMarkers(entries);
  }, [siteConfig]);

  // Reflect the cover-overlay preference, including changes made in the settings
  // panel while the page is open.
  useEffect(() => {
    if (!siteConfig) return;
    const sync = () => {
      const enabled = siteConfig.coverOverlay ? isCoverOverlayEnabled(siteConfig.key) : false;
      coverOverlayEnabledRef.current = enabled;
      setCoverOverlayEnabled(enabled);
      scanAndRender();
    };
    sync();
    return onSettingsChange(sync);
  }, [siteConfig, scanAndRender]);

  const queryDownloadStates = useCallback((keys: string[]) => {
    if (!siteConfig?.downloadTask || keys.length === 0) return;
    if (!isConnected() && !__DEV__) return;

    const { thirdPartyId } = siteConfig.downloadTask;
    const baseUrl = getApiBaseUrl();
    const generation = getApiAddressGeneration();
    httpRequest({
      method: 'POST',
      url: `${baseUrl}/download-task/keys/query`,
      data: { thirdPartyId, keys },
      onSuccess: (result: any) => {
        if (baseUrl !== getApiBaseUrl() || generation !== getApiAddressGeneration()) return;
        // Mark every queried key as resolved so it is not re-queried on the next scroll.
        for (const k of keys) {
          if (!downloadStatesRef.current.has(k)) downloadStatesRef.current.set(k, null);
        }
        if (result.data) {
          for (const item of result.data) {
            if (!item.key) continue;
            downloadStatesRef.current.set(item.key, {
              taskIds: item.taskIds ?? [],
              status: item.status ?? null,
              downloadedAt: item.downloadedAt ?? null,
            });
          }
        }
        scanAndRender();
      },
    });
  }, [siteConfig, scanAndRender]);

  const queryStatus = useCallback((contentIds: string[]) => {
    if (!siteConfig || contentIds.length === 0) return;
    if (!isConnected() && !__DEV__) return;

    const filter = siteConfig.extractFilter(window.location.href);
    const baseUrl = getApiBaseUrl();
    const generation = getApiAddressGeneration();

    httpRequest({
      method: 'POST',
      url: `${baseUrl}/third-party-content-tracker/query`,
      data: { domainKey: siteConfig.key, filter, contentIds },
      onSuccess: (result: any) => {
        if (baseUrl !== getApiBaseUrl() || generation !== getApiAddressGeneration()) return;
        if (result.code !== 0 || !Array.isArray(result.data)) return;
        for (const item of result.data) {
          applyQueriedContentStatus(statusMapRef.current, priorViewStatesRef.current, item.contentId, {
            isViewed: item.isViewed,
            hasUpdate: item.hasUpdate,
            viewedAt: item.viewedAt ? new Date(item.viewedAt) : null,
            updatedAt: item.updatedAt ? new Date(item.updatedAt) : null,
          });
        }
        scanAndRender();
        // The site's timer may have fired while history was still unresolved.
        markVisibleRef.current();
      },
    });
  }, [siteConfig, scanAndRender]);

  const collectNewDownloadKeys = useCallback((elements: HTMLElement[]): string[] => {
    const dl = siteConfig?.downloadTask;
    if (!dl) return [];
    const keys: string[] = [];
    for (const el of elements) {
      const key = dl.extractUrl(el);
      if (key && !downloadStatesRef.current.has(key) && !keys.includes(key)) {
        keys.push(key);
      }
    }
    return keys;
  }, [siteConfig]);

  useEffect(() => {
    if (statusBaseUrlRef.current === connection.baseUrl && statusGenerationRef.current === apiGeneration) return;
    statusBaseUrlRef.current = connection.baseUrl;
    statusGenerationRef.current = apiGeneration;
    statusMapRef.current.clear();
    downloadStatesRef.current.clear();
    priorViewStatesRef.current.clear();
    markingIdsRef.current.clear();
    scanAndRender();
  }, [connection.baseUrl, apiGeneration, scanAndRender]);

  // The initial scan can happen before the health probe succeeds. Retry unresolved
  // items on recovery, including a saved address change, without a page reload.
  useEffect(() => {
    if (!connected || !siteConfig) return;
    const elements = siteConfig.findContents(document);
    const ids = elements.map((el) => siteConfig.extractContentInfo(el).id)
      .filter((id): id is string => !!id && !statusMapRef.current.has(id));
    queryStatus([...new Set(ids)]);
    queryDownloadStates(collectNewDownloadKeys(elements));
  }, [connected, connection.baseUrl, apiGeneration, siteConfig, queryStatus, queryDownloadStates, collectNewDownloadKeys]);

  // The download list changed for this item (added or removed). Re-ask the backend
  // instead of guessing: the answer carries the new task ids, which the next click
  // needs to be able to undo the change.
  const refreshDownloadState = useCallback((element: HTMLElement) => {
    const dl = siteConfig?.downloadTask;
    if (!dl) return;
    const key = dl.extractUrl(element);
    if (!key) return;
    downloadStatesRef.current.delete(key);
    queryDownloadStates([key]);
  }, [siteConfig, queryDownloadStates]);

  const markVisibleAsViewed = useCallback(() => {
    if (!siteConfig) return;
    if (!isConnected() && !__DEV__) return;

    const elements = siteConfig.findContents(document);
    const visibleUnviewed: Array<{ contentId: string; updatedAt: Date | null }> = [];

    for (const element of elements) {
      const info = siteConfig.extractContentInfo(element);
      if (!info.id) continue;

      const status = statusMapRef.current.get(info.id);
      const contentDate = validContentDate(info.updateTime);
      if (!canAutoMarkContent(status, priorViewStatesRef.current.has(info.id), contentDate) || markingIdsRef.current.has(info.id)) continue;
      if (!isElementInViewport(element)) continue;

      visibleUnviewed.push({ contentId: info.id, updatedAt: contentDate });
    }

    if (visibleUnviewed.length === 0) return;

    const filter = siteConfig.extractFilter(window.location.href);
    const baseUrl = getApiBaseUrl();
    const generation = getApiAddressGeneration();
    for (const item of visibleUnviewed) markingIdsRef.current.add(item.contentId);
    const clearPending = () => {
      if (baseUrl !== getApiBaseUrl() || generation !== getApiAddressGeneration()) return;
      for (const item of visibleUnviewed) markingIdsRef.current.delete(item.contentId);
    };
    httpRequest({
      method: 'POST',
      url: `${baseUrl}/third-party-content-tracker/mark-viewed`,
      data: {
        domainKey: siteConfig.key,
        filter,
        contentItems: visibleUnviewed.map((i) => ({
          contentId: i.contentId,
          updatedAt: i.updatedAt?.toISOString() ?? null,
        })),
      },
      onSuccess: (result: any) => {
        if (baseUrl !== getApiBaseUrl() || generation !== getApiAddressGeneration()) return;
        clearPending();
        if (result.code !== 0) return;
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
      onError: clearPending,
    });
  }, [siteConfig, scanAndRender]);
  markVisibleRef.current = markVisibleAsViewed;

  const browsingPage = useMemo(() => siteConfig && ['exhentai', 'soulplus'].includes(siteConfig.key)
    ? buildBrowsingPage(markers.map(({ id, updateTime }) => ({ id, updateTime })), priorViewStatesRef.current,
      siteConfig.extractFilter(window.location.href), siteConfig.contentTimePrecision ?? 'minute')
    : undefined, [siteConfig, markers]);

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
    if (newDlKeys.length > 0) queryDownloadStates(newDlKeys);

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
        if (dlKeys.length > 0) queryDownloadStates(dlKeys);
      }, 300);
    };
    window.addEventListener('scroll', handleScroll);

    // Site-specific mark-viewed setup
    siteConfig.onMarkViewed(markVisibleAsViewed);

    return () => {
      window.removeEventListener('scroll', handleScroll);
      clearTimeout(scrollTimeout);
    };
  }, [siteConfig, scanAndRender, queryStatus, markVisibleAsViewed, collectNewDownloadKeys, queryDownloadStates]);

  // Frame the content cards the user has visited before.
  useEffect(() => {
    for (const m of markers) {
      applyVisitedHighlight(m.element, m.visitedBefore, m.status.hasUpdate);
    }
  }, [markers]);

  if (!siteConfig && !__DEV__) return null;

  // One overlay per cover: whichever action is the site's primary one.
  const renderCoverOverlay = (m: MarkerEntry) => {
    if (!siteConfig) return null;
    if (siteConfig.downloadTask) {
      return (
        <DownloadTaskButton
          overlay
          adapter={siteConfig.downloadTask}
          element={m.element}
          state={m.downloadState}
          onChanged={() => refreshDownloadState(m.element)}
        />
      );
    }
    if (siteConfig.parseTask) {
      return (
        <ParseTaskButton
          overlay
          adapter={siteConfig.parseTask}
          postUrl={siteConfig.parseTask.extractPostUrl(m.element)}
        />
      );
    }
    return null;
  };

  return (
    <>
      <SettingsPanel
        siteKey={siteConfig?.key}
        coverOverlay={siteConfig?.coverOverlay}
        taskSummaryTarget={taskSummaryTarget}
        browsingPage={browsingPage}
        connection={connection}
      />
      {(connected || __DEV__) && siteConfig && markers.flatMap((m) => {
        // The overlay *is* the primary action for this item, so drop the chip that
        // would duplicate it. Items with no cover (list rows, minimal view) keep it.
        const coveredByOverlay = coverOverlayEnabled && m.coverContainer !== null;
        const portals = [
          createPortal(
            <div style={{ pointerEvents: 'auto', display: 'inline-flex', gap: 4, alignItems: 'center' }}>
              {/* Shared action buttons rendered from adapter declarations */}
              <ContentTrackerBadge status={m.status} />
              {siteConfig.parseTask && !coveredByOverlay && (
                <ParseTaskButton
                  adapter={siteConfig.parseTask}
                  postUrl={siteConfig.parseTask.extractPostUrl(m.element)}
                />
              )}
              {siteConfig.downloadTask && !coveredByOverlay && (
                <DownloadTaskButton
                  adapter={siteConfig.downloadTask}
                  element={m.element}
                  state={m.downloadState}
                  onChanged={() => refreshDownloadState(m.element)}
                />
              )}
              {/* Site-specific custom marker (overlays, etc.) */}
              {siteConfig.renderMarker?.(m.element, m.status)}
            </div>,
            m.container,
            `${m.id}-marker`,
          ),
        ];
        if (coveredByOverlay) {
          portals.push(createPortal(renderCoverOverlay(m), m.coverContainer!, `${m.id}-cover`));
        }
        return portals;
      })}
    </>
  );
}
