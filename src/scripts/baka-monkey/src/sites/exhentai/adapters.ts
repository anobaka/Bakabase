import type { DownloadTaskAdapter } from '../../types';
import { getApiBaseUrl, httpRequest } from '../../api';

export function extractGalleryUrl(element: HTMLElement): string {
  // Try all view mode link selectors
  const link =
    element.querySelector<HTMLAnchorElement>('.gl3t a') ??       // Thumbnail
    element.querySelector<HTMLAnchorElement>('.gl3m.glname > a') ?? // Minimal
    element.querySelector<HTMLAnchorElement>('.gl3c.glname > a') ?? // Compact
    element.querySelector<HTMLAnchorElement>('.gl1e a');            // Extended
  return link?.href ?? '';
}

export const exhentaiDownloadTask: DownloadTaskAdapter = {
  extractUrl: extractGalleryUrl,
  createTask: (url) =>
    new Promise<void>((resolve, reject) => {
      httpRequest({
        method: 'POST',
        url: `${getApiBaseUrl()}/download-task/exhentai`,
        data: { type: 1, link: url },
        onSuccess: (result: any) => {
          if (result.code) {
            reject(new Error(result.message));
          } else {
            resolve();
          }
        },
        onError: () => reject(new Error('Network error')),
      });
    }),
};
