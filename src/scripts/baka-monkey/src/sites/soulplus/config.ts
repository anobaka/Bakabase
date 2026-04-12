import type { SiteConfig } from '../../types';
import { extractPostUrl, soulplusParseTask } from './adapters';

function isListModeTr(el: HTMLElement): boolean {
  return el.tagName === 'TR';
}

export const soulplusConfig: SiteConfig = {
  key: 'soulplus',
  domains: ['north-plus.net'],

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
    return { id: url, updateTime: null };
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
};
