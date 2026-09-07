import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import { Tooltip } from '@heroui/tooltip';
import { MdOutlineFileDownload, MdOutlinePlaylistRemove } from 'react-icons/md';
import type { DownloadTaskAdapter, DownloadTaskState } from '../types';
import { getApiBaseUrl, httpRequest } from '../api';
import { showToast } from '../components/Toast';
import { getOverlayRoot } from '../overlay';
import { CoverActionOverlay } from './CoverActionOverlay';
import { t, onLocaleChange } from '../i18n';

/** `DownloadTaskDbModelStatus.Complete` — the task finished, nothing is queued anymore. */
const STATUS_COMPLETE = 300;

function formatTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

function deleteTasks(thirdPartyId: number, taskIds: number[]): Promise<void> {
  return new Promise((resolve, reject) => {
    httpRequest({
      method: 'DELETE',
      url: `${getApiBaseUrl()}/download-task`,
      data: { ids: taskIds, thirdPartyId },
      onSuccess: (result: any) => {
        if (result.code) reject(new Error(result.message));
        else resolve();
      },
      onError: () => reject(new Error('Network error')),
    });
  });
}

export function DownloadTaskButton({
  adapter,
  element,
  state,
  onChanged,
  overlay,
}: {
  adapter: DownloadTaskAdapter;
  element: HTMLElement;
  /** Backend view of this item, or null while it has not been queried yet. */
  state?: DownloadTaskState | null;
  /** Called after the download list changed, so the marker can re-query. */
  onChanged?: () => void;
  /** Render as the full-cover overlay instead of the small chip button. */
  overlay?: boolean;
}) {
  const [loading, setLoading] = useState(false);
  const [hovered, setHovered] = useState(false);
  const [, forceUpdate] = useState(0);

  useEffect(() => onLocaleChange(() => forceUpdate((n) => n + 1)), []);

  const url = adapter.extractUrl(element);
  const taskIds = state?.taskIds ?? [];
  // A task in the list is the undo handle for an accidental add: clicking again
  // removes it. The permanent download record (`downloadedAt`) is not — it survives
  // task deletion on purpose, and only serves as a "you had this already" warning.
  const isQueued = taskIds.length > 0;
  const downloadedAt = state?.downloadedAt ?? null;

  const handleAdd = async () => {
    if (!url) {
      alert(t('downloadLinkNotFound'));
      return;
    }
    setLoading(true);
    try {
      await adapter.createTask(url);
      showToast(t('addedToDownloadQueue'));
      onChanged?.();
    } catch {
      alert(t('downloadFailed'));
    } finally {
      setLoading(false);
    }
  };

  const handleRemove = async () => {
    setLoading(true);
    try {
      await deleteTasks(adapter.thirdPartyId, taskIds);
      showToast(t('removedFromDownloadList'));
      onChanged?.();
    } catch {
      alert(t('requestFailed'));
    } finally {
      setLoading(false);
    }
  };

  const handleClick = isQueued ? handleRemove : handleAdd;

  const label = isQueued
    ? (state?.status === STATUS_COMPLETE ? t('removeFromDownloadList') : t('cancelDownloadTask'))
    : t('download');
  const Icon = isQueued ? MdOutlinePlaylistRemove : MdOutlineFileDownload;
  // Amber while it sits in the list, muted once it was downloaded but is no longer
  // queued, plain primary for an item the backend has never seen.
  const color = isQueued ? 'warning' : (downloadedAt ? 'default' : 'primary');

  if (overlay) {
    return (
      <CoverActionOverlay
        label={label}
        icon={Icon}
        tone={isQueued ? 'danger' : 'primary'}
        busy={loading}
        href={url || undefined}
        onActivate={handleClick}
      />
    );
  }

  const tooltipLines = [label];
  if (downloadedAt) tooltipLines.push(t('alreadyDownloadedAt', { time: formatTime(downloadedAt) }));
  const tooltipContent = tooltipLines.length > 1
    ? <div style={{ whiteSpace: 'pre-line', textAlign: 'center' }}>{tooltipLines.join('\n')}</div>
    : label;

  return (
    <Tooltip content={tooltipContent} placement="top" size="sm" color="foreground" portalContainer={getOverlayRoot()}>
      <Button
        size="sm"
        color={color}
        variant={!loading && hovered ? 'solid' : 'flat'}
        isIconOnly
        isDisabled={loading}
        onPress={handleClick}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      >
        <Icon size="1.2em" />
      </Button>
    </Tooltip>
  );
}
