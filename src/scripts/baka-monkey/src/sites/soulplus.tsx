import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import type { SiteConfig } from '../types';
import { getApiBaseUrl, httpRequest } from '../api';
import { showToast } from '../components/Toast';
import { t } from '../i18n';

function ParseButton({ targetId, postUrl }: { targetId: number; postUrl: string }) {
  const handleClick = () => {
    httpRequest({
      method: 'POST',
      url: `${getApiBaseUrl()}/post-parser/task`,
      data: { sourceLinksMap: { '5': [postUrl] }, targets: [targetId] },
      onSuccess: () => showToast(t('addedToParseQueue')),
      onError: () => alert(t('requestFailed')),
    });
  };

  const label = targetId === 1
    ? t('parseDownloadInfo')
    : t('parseTarget', { id: targetId });

  return (
    <Button size="sm" color="primary" variant="flat" onPress={handleClick}>
      {label}
    </Button>
  );
}

function SoulplusMarker({ element }: { element: HTMLElement }) {
  const [targets, setTargets] = useState<number[] | null>(null);

  const linkEl = element.querySelector<HTMLAnchorElement>('.section-title > a')
    || element.querySelector<HTMLAnchorElement>('.section-text div > a');
  const postUrl = linkEl
    ? new URL(linkEl.getAttribute('href') || '', window.location.origin).href
    : '';

  useEffect(() => {
    if (!postUrl) return;

    httpRequest<{ data?: number[] }>({
      method: 'GET',
      url: `${getApiBaseUrl()}/post-parser/targets`,
      onSuccess: (result) => setTargets(result.data || []),
    });
  }, [postUrl]);

  if (!postUrl || !targets || targets.length === 0) return null;

  return (
    <div className="bakabase-parse-btn" style={{ display: 'inline-flex', gap: 4, marginLeft: 8 }}>
      {targets.map((targetId) => (
        <ParseButton key={targetId} targetId={targetId} postUrl={postUrl} />
      ))}
    </div>
  );
}

export const soulplusConfig: SiteConfig = {
  key: 'soulplus',
  domains: ['north-plus.net'],

  extractFilter: () => null,

  findContents: (doc) =>
    Array.from(doc.querySelectorAll<HTMLElement>('#thread_img .inner')),

  extractContentInfo: (element) => {
    const linkEl = element.querySelector<HTMLAnchorElement>('.section-title > a')
      || element.querySelector<HTMLAnchorElement>('.section-text div > a');
    if (!linkEl) return { id: null, updateTime: null };

    const url = new URL(linkEl.getAttribute('href') || '', window.location.origin).href;
    return { id: url, updateTime: null };
  },

  onMarkViewed: () => {},

  renderMarker: (element) => (
    <SoulplusMarker element={element} />
  ),
};
