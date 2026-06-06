import { useState } from 'react';
import { Button } from '@heroui/button';
import { Tooltip } from '@heroui/tooltip';
import { MdOutlineFileDownload } from 'react-icons/md';
import type { DownloadTaskAdapter } from '../types';
import { showToast } from '../components/Toast';
import { getOverlayRoot } from '../overlay';
import { t } from '../i18n';

function formatTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

export function DownloadTaskButton({
  adapter,
  element,
  downloadedAt,
  onDownloaded,
}: {
  adapter: DownloadTaskAdapter;
  element: HTMLElement;
  /** When set, this item was already downloaded before (ISO timestamp). */
  downloadedAt?: string | null;
  /** Called after a download task is successfully created, so the marker can update immediately. */
  onDownloaded?: () => void;
}) {
  const [loading, setLoading] = useState(false);
  const [hovered, setHovered] = useState(false);

  const handleClick = async () => {
    const url = adapter.extractUrl(element);
    if (!url) {
      alert(t('downloadLinkNotFound'));
      return;
    }
    setLoading(true);
    try {
      await adapter.createTask(url);
      showToast(t('addedToDownloadQueue'));
      onDownloaded?.();
    } catch {
      alert(t('downloadFailed'));
    } finally {
      setLoading(false);
    }
  };

  const isDownloaded = !!downloadedAt;
  const tooltipContent = isDownloaded ? (
    <div style={{ whiteSpace: 'pre-line', textAlign: 'center' }}>
      {`${t('download')}\n${t('alreadyDownloadedAt', { time: formatTime(downloadedAt!) })}`}
    </div>
  ) : (
    t('download')
  );

  return (
    <Tooltip content={tooltipContent} placement="top" size="sm" color="foreground" portalContainer={getOverlayRoot()}>
      <Button
        size="sm"
        color={isDownloaded ? 'warning' : 'primary'}
        variant={!loading && hovered ? 'solid' : 'flat'}
        isIconOnly
        isDisabled={loading}
        onPress={handleClick}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      >
        <MdOutlineFileDownload size="1.2em" />
      </Button>
    </Tooltip>
  );
}
