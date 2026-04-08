import { useState } from 'react';
import type { SiteConfig, ContentStatus } from '../types';
import { Button } from '@heroui/button';
import { Chip } from '@heroui/chip';
import { getApiBaseUrl, httpRequest } from '../api';
import { showToast } from '../components/Toast';
import { t } from '../i18n';

function DownloadButton({ element }: { element: HTMLElement }) {
  const handleClick = () => {
    const link = element.querySelector<HTMLAnchorElement>('.gl3t a');
    if (!link) { alert(t('downloadLinkNotFound')); return; }

    httpRequest({
      method: 'POST',
      url: `${getApiBaseUrl()}/download-task/exhentai`,
      data: { type: 1, link: link.href },
      onSuccess: (result: any) => {
        if (result.code) { alert(result.message); return; }
        showToast(t('addedToDownloadQueue'));
      },
      onError: () => alert(t('downloadFailed')),
    });
  };

  return (
    <div className="bakabase-download-btn" style={{ position: 'absolute', bottom: 2, right: 2, zIndex: 101, pointerEvents: 'auto' }}>
      <Button size="sm" color="primary" variant="solid" onPress={handleClick}>
        {t('download')}
      </Button>
    </div>
  );
}

function StatusMarker({ status }: { status: ContentStatus }) {
  if (!status.isViewed) return null;

  const hasUpdate = status.hasUpdate;

  return (
    <div
      className="bakabase-marker"
      style={{ position: 'absolute', top: 2, right: 2, zIndex: 100, pointerEvents: 'none' }}
    >
      <Chip size="sm" color={hasUpdate ? 'warning' : 'success'} variant="solid">
        {hasUpdate ? t('updated') : '✓'}
      </Chip>
    </div>
  );
}

function ViewedOverlay({ status }: { status: ContentStatus }) {
  const [hover, setHover] = useState(false);

  if (!status.isViewed || status.hasUpdate) return null;

  return (
    <div
      className="bakabase-viewed-overlay"
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(255, 255, 255, 0.1)',
        zIndex: 99,
        pointerEvents: 'none',
        transition: 'opacity 0.2s',
        opacity: hover ? 0.3 : 1,
      }}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
    />
  );
}

function ExhentaiMarker({ element, status }: { element: HTMLElement; status: ContentStatus }) {
  return (
    <>
      <ViewedOverlay status={status} />
      <DownloadButton element={element} />
      <StatusMarker status={status} />
    </>
  );
}

export const exhentaiConfig: SiteConfig = {
  key: 'exhentai',
  domains: ['exhentai.org'],

  extractFilter: () => null,

  findContents: (doc) =>
    Array.from(doc.querySelectorAll<HTMLElement>('.itg.gld .gl1t')),

  extractContentInfo: (element) => {
    const link = element.querySelector<HTMLAnchorElement>('.gl3t a');
    if (!link) return { id: null, updateTime: null };

    const u = new URL(link.href);
    const galleryId = u.pathname.split('/g/')[1]?.split('/')[0] || '';
    if (!galleryId) return { id: null, updateTime: null };

    const dtId = `posted_${galleryId}`;
    const timeElement = document.getElementById(dtId);
    let updateTime: Date | null = null;

    if (timeElement) {
      const timeText = timeElement.textContent?.trim() ?? '';
      updateTime = new Date(timeText.replace(' ', 'T') + ':00Z');
    }

    return { id: galleryId, updateTime };
  },

  onMarkViewed: (markVisibleFn) => {
    let markTimeout: ReturnType<typeof setTimeout>;
    window.addEventListener('scroll', () => {
      clearTimeout(markTimeout);
      markTimeout = setTimeout(markVisibleFn, 500);
    });
    setTimeout(markVisibleFn, 1000);
  },

  renderMarker: (element, status) => (
    <ExhentaiMarker element={element} status={status} />
  ),
};
