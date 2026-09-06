/**
 * Find the tightest element wrapping an item's thumbnail image, which is what the
 * cover overlay is sized against.
 *
 * Host markup differs per site and per view mode, so this works from the image
 * outwards instead of hard-coding a container: the anchor around it when there is
 * one (it also gives the overlay a real link to fall back on), otherwise the
 * image's own wrapper. Returns null when the item shows no image at all — list and
 * minimal views have no cover to take over.
 */
export function findThumbnailBox(scope: HTMLElement | null): HTMLElement | null {
  if (!scope) return null;

  const img = scope.querySelector<HTMLElement>('img');
  if (!img) return null;

  const anchor = img.closest<HTMLElement>('a');
  if (anchor && scope.contains(anchor)) return anchor;

  const parent = img.parentElement;
  if (parent && scope.contains(parent)) return parent;

  return scope;
}
