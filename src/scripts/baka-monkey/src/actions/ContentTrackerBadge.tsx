import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import { Tooltip } from '@heroui/tooltip';
import { MdOutlineHistory } from 'react-icons/md';
import type { ContentStatus } from '../types';
import { t, onLocaleChange } from '../i18n';

function formatTime(date: Date | null): string {
  if (!date) return '-';
  return date.toLocaleString();
}

export function ContentTrackerBadge({ status }: { status: ContentStatus }) {
  const [, forceUpdate] = useState(0);
  useEffect(() => onLocaleChange(() => forceUpdate((n) => n + 1)), []);

  if (!status.isViewed) return null;

  const tooltip = `${t('lastViewedAt')}: ${formatTime(status.viewedAt)}`;

  return (
    <Tooltip content={tooltip} placement="top" size="sm" color="foreground">
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
