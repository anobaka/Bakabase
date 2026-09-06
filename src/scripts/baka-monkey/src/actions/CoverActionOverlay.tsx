import type { MouseEvent } from 'react';
import type { IconType } from 'react-icons';
import { MdOutlineAutorenew } from 'react-icons/md';
import { t } from '../i18n';

export type CoverActionTone = 'primary' | 'danger' | 'warning' | 'default';

/**
 * A transparent layer covering an item's thumbnail. It is always click-catching, so
 * the whole cover acts as one big button; hovering reveals what that button does.
 *
 * Taking over the cover costs the host's own "click the cover to open it" gesture,
 * so modifier and middle clicks are routed to `href` instead of the action — the
 * item stays reachable without aiming at its title.
 */
export function CoverActionOverlay({
  label,
  icon: Icon,
  tone,
  busy,
  disabled,
  href,
  onActivate,
}: {
  label: string;
  icon: IconType;
  tone: CoverActionTone;
  /** The action is running: show a spinner and swallow further clicks. */
  busy?: boolean;
  /** Nothing to do on click (e.g. a task the backend is still working on). */
  disabled?: boolean;
  /** The item's own page, opened on modifier / middle click. */
  href?: string;
  onActivate: () => void;
}) {
  const open = (event: MouseEvent, newTab: boolean) => {
    event.preventDefault();
    event.stopPropagation();
    if (!href) return;
    if (newTab) {
      window.open(href, '_blank', 'noopener');
    } else {
      window.location.href = href;
    }
  };

  const handleClick = (event: MouseEvent) => {
    if (href && (event.ctrlKey || event.metaKey || event.shiftKey)) {
      open(event, event.ctrlKey || event.metaKey);
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    if (busy || disabled) return;
    onActivate();
  };

  // Middle click never reaches onClick; without this it would fall through to the
  // host anchor when the overlay happens to sit inside one, and do nothing otherwise.
  const handleAuxClick = (event: MouseEvent) => {
    if (event.button === 1) open(event, true);
  };

  return (
    <div
      className="bk-cover-overlay"
      data-tone={tone}
      data-disabled={disabled ? 'true' : undefined}
      role="button"
      aria-label={label}
      onClick={handleClick}
      onAuxClick={handleAuxClick}
      // Stops the host page from starting a text/image drag on the cover.
      onMouseDown={(e) => e.preventDefault()}
    >
      <div className="bk-cover-overlay-body">
        {busy
          ? <MdOutlineAutorenew className="bk-cover-overlay-icon bk-spin" />
          : <Icon className="bk-cover-overlay-icon" />}
        <span className="bk-cover-overlay-label">{label}</span>
        {href && <span className="bk-cover-overlay-hint">{t('coverOverlayOpenHint')}</span>}
      </div>
    </div>
  );
}
