import type { ParseTaskAdapter } from '../../types';

export function extractPostUrl(element: HTMLElement): string {
  // Image wall mode: .inner element
  const wallLink = element.querySelector<HTMLAnchorElement>('.section-title > a')
    || element.querySelector<HTMLAnchorElement>('.section-text div > a');
  if (wallLink) {
    return new URL(wallLink.getAttribute('href') || '', window.location.origin).href;
  }

  // List mode: tr element - find link inside h3 > a
  const listLink = element.querySelector<HTMLAnchorElement>('td h3 > a[href*="read.php"]');
  if (listLink) {
    return new URL(listLink.getAttribute('href') || '', window.location.origin).href;
  }

  return '';
}

export const soulplusParseTask: ParseTaskAdapter = {
  source: 5, // PostParserSource.SoulPlus
  targets: [1], // PostParseTarget.DownloadInfo
  extractPostUrl,
};
