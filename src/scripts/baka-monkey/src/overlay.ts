export const OVERLAY_ROOT_ID = 'bk-overlay-root';

/**
 * Container for HeroUI overlays (Popover / Tooltip / Select).
 *
 * Without this, overlays portal to the bare <body>, where the scoped reset in
 * index.css does not reach — so they would render unstyled now that Tailwind's
 * global preflight is no longer injected. Created in main.tsx before render.
 */
export function getOverlayRoot(): HTMLElement | undefined {
  return document.getElementById(OVERLAY_ROOT_ID) ?? undefined;
}
