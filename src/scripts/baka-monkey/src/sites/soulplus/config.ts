import type { SiteConfig } from '../../types';
import { findThumbnailBox } from '../../utils/cover';
import { t } from '../../i18n';
import { extractPostUrl, soulplusParseTask } from './adapters';

function isListModeTr(el: HTMLElement): boolean {
  return el.tagName === 'TR';
}

/** The author column/image-wall footer exposes the original post date, without a time. */
export function extractSoulplusPostDate(element: HTMLElement): Date | null {
  const dateElement = isListModeTr(element)
    ? element.querySelector('td.tal.y-style .f10.gray2')
    : element.querySelector('.section-intro table tr td:last-child');
  const text = dateElement?.textContent?.trim() ?? '';
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text);
  if (!match) return null;
  const [year, month, day] = match.slice(1).map(Number);
  const calendarDate = new Date(`${text}T00:00:00Z`);
  if (year < 1 || !Number.isFinite(calendarDate.getTime()) || calendarDate.getUTCFullYear() !== year ||
    calendarDate.getUTCMonth() + 1 !== month || calendarDate.getUTCDate() !== day) return null;
  // Keep the site's date in its own timezone; midnight is a day anchor, not a known posting time.
  return new Date(`${text}T00:00:00+08:00`);
}

function findPostCover(element: HTMLElement): HTMLElement | null {
  // List mode rows show no thumbnail; only the image wall has a cover.
  if (isListModeTr(element)) return null;
  return findThumbnailBox(element);
}

export const soulplusConfig: SiteConfig = {
  key: 'soulplus',
  domains: ['north-plus.net'],
  contentTimePrecision: 'day',

  extractFilter: () => null,

  findContents: (doc) => {
    // Image wall mode
    const wallItems = Array.from(doc.querySelectorAll<HTMLElement>('#thread_img .inner'));
    if (wallItems.length > 0) return wallItems;

    // List mode: select tr.tr3.t_one that contain a read.php link (actual posts, not announcements/ads)
    const listRows = Array.from(
      doc.querySelectorAll<HTMLElement>('tr.tr3.t_one'),
    ).filter((tr) => {
      const link = tr.querySelector<HTMLAnchorElement>('td h3 > a[href*="read.php"]');
      return link != null;
    });
    return listRows;
  },

  extractContentInfo: (element) => {
    const url = extractPostUrl(element);
    if (!url) return { id: null, updateTime: null };
    return { id: url, updateTime: extractSoulplusPostDate(element) };
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

    if (isListModeTr(element)) {
      // List mode: insert inline after the h3 in the title td, scaled down to avoid expanding the row
      container.style.cssText = 'display:inline-flex;margin-left:8px;vertical-align:middle;transform:scale(0.75);transform-origin:left center;';
      const h3 = element.querySelector('td h3');
      if (h3) {
        h3.parentElement!.insertBefore(container, h3.nextSibling);
      } else {
        element.appendChild(container);
      }
    } else {
      // Image wall mode: position at bottom-right of the <li> wrapper
      container.style.cssText = 'position:absolute;bottom:0;right:0;z-index:98;padding:2px 4px;';
      const li = element.closest('li');
      if (li) {
        if (getComputedStyle(li).position === 'static') {
          li.style.position = 'relative';
        }
        li.appendChild(container);
      } else {
        element.appendChild(container);
      }
    }

    return container;
  },

  parseTask: soulplusParseTask,

  coverOverlay: {
    findCover: findPostCover,
    label: () => t('bigParseButton'),
    description: () => t('bigParseButtonDescription'),
  },
};
