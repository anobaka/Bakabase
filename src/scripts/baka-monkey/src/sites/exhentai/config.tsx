import type { SiteConfig } from '../../types';
import { findThumbnailBox } from '../../utils/cover';
import { t } from '../../i18n';
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

/** ExHentai's posted timestamps are UTC, never the time at which we visited the page. */
function parsePostedTime(text: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?$/.exec(text.trim());
  if (!match) return null;
  const [year, month, day, hour, minute, second = 0] = match.slice(1).map((part) => Number(part ?? 0));
  const date = new Date(`${match[1]}-${match[2]}-${match[3]}T${match[4]}:${match[5]}:${match[6] ?? '00'}Z`);
  if (year < 1 || !Number.isFinite(date.getTime()) || date.getUTCFullYear() !== year ||
    date.getUTCMonth() + 1 !== month || date.getUTCDate() !== day || date.getUTCHours() !== hour ||
    date.getUTCMinutes() !== minute || date.getUTCSeconds() !== second) return null;
  return date;
}

export function extractExhentaiContentDate(element: HTMLElement, mode: ViewMode, galleryId: string): Date | null {
  // Read this item's posted element, so another row/document cannot supply its date.
  if (!/^\d+$/.test(galleryId)) return null;
  const postedEl = element.querySelector(`[id="posted_${galleryId}"]`);
  const postedDate = parsePostedTime(postedEl?.textContent ?? '');
  if (postedDate) return postedDate;

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
  return parsePostedTime(dateText);
}

// Cell holding the gallery thumbnail, per view mode. Minimal shows no thumbnail,
// so there is no cover to turn into a button there.
const COVER_CELL_SELECTORS: Record<ViewMode, string | null> = {
  gld: '.gl3t',
  gltm: null,
  gltc: '.gl1c',
  glte: '.gl1e',
};

function findGalleryCover(element: HTMLElement): HTMLElement | null {
  const mode = getMode(document);
  if (!mode) return null;
  const selector = COVER_CELL_SELECTORS[mode];
  if (!selector) return null;
  return findThumbnailBox(element.querySelector<HTMLElement>(selector));
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

    const updateTime = extractExhentaiContentDate(element, mode, galleryId);
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
    container.className = 'bk-marker';
    container.style.cssText = 'position:absolute;bottom:2px;right:2px;z-index:98;';
    if (getComputedStyle(element).position === 'static') {
      element.style.position = 'relative';
    }
    element.appendChild(container);
    return container;
  },

  downloadTask: exhentaiDownloadTask,

  coverOverlay: {
    findCover: findGalleryCover,
    label: () => t('bigDownloadButton'),
    description: () => t('bigDownloadButtonDescription'),
  },
};
