import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { SiteConfig, ContentStatus } from './types';
import { getApiBaseUrl, httpRequest } from './api';
import { SettingsPanel } from './components/SettingsPanel';
import { t } from './i18n';

interface MarkerEntry {
  id: string;
  element: HTMLElement;
  container: HTMLElement;
  status: ContentStatus;
}

const DEFAULT_STATUS: ContentStatus = {
  isViewed: false,
  hasUpdate: false,
  viewedAt: null,
  updatedAt: null,
};

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
  const statusMapRef = useRef(new Map<string, ContentStatus>());
  const siteConfig = useMemo(() => {
    const hostname = window.location.hostname;
    return siteConfigs.find((c) => c.domains.some((d) => hostname.includes(d))) ?? null;
  }, [siteConfigs]);

  const scanAndRender = useCallback(() => {
    if (!siteConfig) return;

    const elements = siteConfig.findContents(document);
    const entries: MarkerEntry[] = [];

    for (const element of elements) {
      const info = siteConfig.extractContentInfo(element);
      if (!info.id) continue;

      // Ensure element has relative positioning for absolute children
      if (getComputedStyle(element).position === 'static') {
        element.style.position = 'relative';
      }

      // Create or reuse a container for the React portal
      let container = element.querySelector<HTMLElement>('.bakabase-react-root');
      if (!container) {
        container = document.createElement('div');
        container.className = 'bakabase-react-root';
        container.style.cssText = 'position:absolute;top:0;left:0;right:0;bottom:0;pointer-events:none;z-index:98;';
        // Allow interactive children to receive events
        container.addEventListener('click', (e) => e.stopPropagation(), true);
        element.appendChild(container);
      }

      const status = statusMapRef.current.get(info.id) ?? DEFAULT_STATUS;
      entries.push({ id: info.id, element, container, status });
    }

    setMarkers(entries);
  }, [siteConfig]);

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
        }
        scanAndRender();
      },
    });
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
      }, 300);
    };
    window.addEventListener('scroll', handleScroll);

    // Site-specific mark-viewed setup
    siteConfig.onMarkViewed(markVisibleAsViewed);

    return () => {
      window.removeEventListener('scroll', handleScroll);
      clearTimeout(scrollTimeout);
    };
  }, [siteConfig, scanAndRender, queryStatus, markVisibleAsViewed]);

  if (!siteConfig && !__DEV__) return null;

  return (
    <>
      <SettingsPanel />
      {siteConfig && markers.map((m) =>
        createPortal(
          <div style={{ pointerEvents: 'auto' }}>
            {siteConfig.renderMarker(m.element, m.status)}
          </div>,
          m.container,
        ),
      )}
    </>
  );
}
