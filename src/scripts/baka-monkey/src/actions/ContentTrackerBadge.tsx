import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import { Tooltip } from '@heroui/tooltip';
import { MdOutlineHistory } from 'react-icons/md';
import type { ContentStatus } from '../types';
import { getOverlayRoot } from '../overlay';
import { getEffectiveTimeZone, onTimeZoneChange } from '../timezone';
import { t, onLocaleChange } from '../i18n';

function formatTime(date: Date | null): string {
  if (!date) return '-';
  // Timestamps are stored in UTC; render them in the user's timezone (detected
  // from the browser, or overridden in the settings panel).
  try {
    return date.toLocaleString(undefined, {
      timeZone: getEffectiveTimeZone(),
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      timeZoneName: 'short',
    });
  } catch {
    return date.toLocaleString();
  }
}

export function ContentTrackerBadge({ status }: { status: ContentStatus }) {
  const [, forceUpdate] = useState(0);
  useEffect(() => onLocaleChange(() => forceUpdate((n) => n + 1)), []);
  useEffect(() => onTimeZoneChange(() => forceUpdate((n) => n + 1)), []);

  if (!status.isViewed) return null;

  const tooltip = `${t('lastViewedAt')}: ${formatTime(status.viewedAt)}`;

  return (
    <Tooltip content={tooltip} placement="top" size="sm" color="foreground" portalContainer={getOverlayRoot()}>
      <Button
        size="sm"
        color={status.hasUpdate ? 'warning' : 'success'}
        variant="light"
        isIconOnly
      >
        <MdOutlineHistory size="1.2em" />
      </Button>
    </Tooltip>
  );
}
