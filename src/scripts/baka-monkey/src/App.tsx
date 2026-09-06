import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { CoverOverlayConfig, SiteConfig, ContentStatus, DownloadTaskState } from './types';
import { getApiBaseUrl, httpRequest } from './api';
import { SettingsPanel } from './components/SettingsPanel';
import { ParseTaskButton } from './actions/ParseTaskButton';
import { DownloadTaskButton } from './actions/DownloadTaskButton';
import { ContentTrackerBadge } from './actions/ContentTrackerBadge';
import { startHeartbeat, isConnected, onConnectionChange } from './heartbeat';
import { isCoverOverlayEnabled, onSettingsChange } from './settings';
import { t } from './i18n';

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
  const [connected, setConnected] = useState(isConnected);
  const [coverOverlayEnabled, setCoverOverlayEnabled] = useState(false);
  const statusMapRef = useRef(new Map<string, ContentStatus>());
  // downloadKey (== adapter.extractUrl) -> backend state, or null when the backend
  // knows nothing about it (queried, never downloaded, not queued).
  const downloadStatesRef = useRef(new Map<string, DownloadTaskState | null>());
  // Read inside scanAndRender, which must not change identity when the setting is
  // toggled: the scan effect below re-registers listeners whenever it does.
  const coverOverlayEnabledRef = useRef(false);
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

      let downloadState: DownloadTaskState | null = null;
      if (siteConfig.downloadTask) {
        const dlKey = siteConfig.downloadTask.extractUrl(element);
        if (dlKey) downloadState = downloadStatesRef.current.get(dlKey) ?? null;
      }

      const coverContainer = siteConfig.coverOverlay && coverOverlayEnabledRef.current
        ? ensureCoverContainer(element, siteConfig.coverOverlay)
        : null;

      const visitedBefore = visitedBeforeRef.current.has(info.id);
      entries.push({ id: info.id, element, container, status, downloadState, coverContainer, visitedBefore });
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

    const { thirdPartyId } = siteConfig.downloadTask;
    httpRequest({
      method: 'POST',
      url: `${getApiBaseUrl()}/download-task/keys/query`,
      data: { thirdPartyId, keys },
      onSuccess: (result: any) => {
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
      if (key && !downloadStatesRef.current.has(key) && !keys.includes(key)) {
        keys.push(key);
      }
    }
    return keys;
  }, [siteConfig]);

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
        connected={connected}
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
