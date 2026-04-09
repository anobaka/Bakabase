import type { SiteConfig } from '../../types';
import { exhentaiDownloadTask } from './adapters';

type ViewMode = 'gld' | 'gltm' | 'gltc' | 'glte';

function detectViewMode(doc: Document): ViewMode | null {
  const container = doc.querySelector('.itg');
  if (!container) return null;
  for (const cls of Array.from(container.classList)) {
    if (cls === 'gld' || cls === 'gltm' || cls === 'gltc' || cls === 'glte') {
      return cls;
    }
  }
  return null;
}

function findContentsByMode(doc: Document, mode: ViewMode): HTMLElement[] {
  switch (mode) {
    case 'gld':
      // Thumbnail: direct children of .itg.gld
      return Array.from(doc.querySelectorAll<HTMLElement>('.itg.gld > .gl1t'));
    case 'gltm':
    case 'gltc':
      // Minimal / Compact: table rows, skip first (header)
      return Array.from(doc.querySelectorAll<HTMLElement>(`.itg.${mode} > tbody > tr`)).slice(1);
    case 'glte':
      // Extended: table rows, no header
      return Array.from(doc.querySelectorAll<HTMLElement>('.itg.glte > tbody > tr'));
  }
}

function extractGalleryLink(element: HTMLElement, mode: ViewMode): HTMLAnchorElement | null {
  switch (mode) {
    case 'gld':
      return element.querySelector<HTMLAnchorElement>('.gl3t a');
    case 'gltm':
      return element.querySelector<HTMLAnchorElement>('.gl3m.glname > a');
    case 'gltc':
      return element.querySelector<HTMLAnchorElement>('.gl3c.glname > a');
    case 'glte':
      return element.querySelector<HTMLAnchorElement>('.gl1e a');
  }
}

function extractUpdateTime(element: HTMLElement, mode: ViewMode, galleryId: string): Date | null {
  // All modes have a posted_{id} element we can use
  const postedEl = document.getElementById(`posted_${galleryId}`);
  if (postedEl) {
    const text = postedEl.textContent?.trim() ?? '';
    if (text) return new Date(text.replace(' ', 'T') + ':00Z');
  }

  // Fallback: parse from mode-specific location
  let dateText = '';
  switch (mode) {
    case 'gld': {
      const cells = element.querySelectorAll('.gl5t > div:first-child > div');
      dateText = cells[1]?.textContent?.trim() ?? '';
      break;
    }
    case 'gltm': {
      const divs = element.querySelectorAll('.gl2m > div');
      dateText = divs[divs.length - 1]?.textContent?.trim() ?? '';
      break;
    }
    case 'gltc': {
      const last = element.querySelector('.gl2c > div:last-child');
      dateText = last?.firstElementChild?.textContent?.trim() ?? '';
      break;
    }
    case 'glte': {
      const divs = element.querySelectorAll('.gl3e > div');
      dateText = divs[1]?.textContent?.trim() ?? '';
      break;
    }
  }
  if (dateText) return new Date(dateText.replace(' ', 'T') + ':00Z');
  return null;
}

// Cache detected mode so all functions use the same value
let cachedMode: ViewMode | null = null;

function getMode(doc: Document): ViewMode | null {
  if (cachedMode === null) {
    cachedMode = detectViewMode(doc);
  }
  return cachedMode;
}

export const exhentaiConfig: SiteConfig = {
  key: 'exhentai',
  domains: ['exhentai.org'],

  extractFilter: () => null,

  findContents: (doc) => {
    const mode = getMode(doc);
    if (!mode) return [];
    return findContentsByMode(doc, mode);
  },

  extractContentInfo: (element) => {
    const mode = getMode(document);
    if (!mode) return { id: null, updateTime: null };

    const link = extractGalleryLink(element, mode);
    if (!link) return { id: null, updateTime: null };

    const u = new URL(link.href);
    const galleryId = u.pathname.split('/g/')[1]?.split('/')[0] || '';
    if (!galleryId) return { id: null, updateTime: null };

    const updateTime = extractUpdateTime(element, mode, galleryId);
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

  createContainer: (element) => {
    const container = document.createElement('div');
    container.className = 'bakabase-react-root';
    container.style.cssText = 'position:absolute;bottom:2px;right:2px;z-index:98;';
    element.appendChild(container);
    return container;
  },

  downloadTask: exhentaiDownloadTask,
};
