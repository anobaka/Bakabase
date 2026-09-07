import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import { Tooltip } from '@heroui/tooltip';
import {
  MdOutlineDocumentScanner,
  MdOutlinePlaylistRemove,
  MdOutlineRefresh,
} from 'react-icons/md';
import type { ParseTaskAdapter } from '../types';
import { getApiBaseUrl, httpRequest } from '../api';
import { showToast } from '../components/Toast';
import { getOverlayRoot } from '../overlay';
import { CoverActionOverlay, type CoverActionTone } from './CoverActionOverlay';
import { t, onLocaleChange } from '../i18n';
import { createBatcher } from '../utils/batcher';

const enum TaskStatus {
  None = 0,
  Pending = 1,
  Complete = 2,
  Failed = 3,
  Deleted = 4,
}

// Batchers keyed by source, so different sites don't mix
const statusBatchers = new Map<number, ReturnType<typeof createBatcher<string, TaskStatus>>>();

function getStatusBatcher(source: number) {
  let batcher = statusBatchers.get(source);
  if (!batcher) {
    batcher = createBatcher<string, TaskStatus>({
      delay: 100,
      execute: (links) =>
        new Promise((resolve, reject) => {
          httpRequest<{ data?: Record<string, number> }>({
            method: 'POST',
            url: `${getApiBaseUrl()}/post-parser/task/statuses`,
            data: { source, links },
            onSuccess: (result) => {
              const map = new Map<string, TaskStatus>();
              if (result.data) {
                for (const [link, status] of Object.entries(result.data)) {
                  map.set(link, status as TaskStatus);
                }
              }
              resolve(map);
            },
            onError: reject,
          });
        }),
    });
    statusBatchers.set(source, batcher);
  }
  return batcher;
}

type Action = 'add' | 'remove';

interface ButtonSpec {
  label: string;
  action: Action;
  icon: typeof MdOutlineDocumentScanner;
  color: 'primary' | 'secondary' | 'danger';
  tone: CoverActionTone;
}

function getButtonSpec(status: TaskStatus): ButtonSpec {
  // A queued task is the undo handle for an accidental add: clicking it again drops
  // it from the list. Finished tasks keep the re-parse action instead — removing one
  // would throw away results the user asked for.
  if (status === TaskStatus.Pending) {
    return {
      label: t('cancelParseTask'),
      action: 'remove',
      icon: MdOutlinePlaylistRemove,
      color: 'danger',
      tone: 'danger',
    };
  }
  if (status === TaskStatus.Complete || status === TaskStatus.Failed) {
    return {
      label: t('reExtractDownload'),
      action: 'add',
      icon: MdOutlineRefresh,
      color: 'secondary',
      tone: 'warning',
    };
  }
  return {
    label: t('extractDownloadNow'),
    action: 'add',
    icon: MdOutlineDocumentScanner,
    color: 'primary',
    tone: 'primary',
  };
}

export function ParseTaskButton({
  adapter,
  postUrl,
  overlay,
}: {
  adapter: ParseTaskAdapter;
  postUrl: string;
  /** Render as the full-cover overlay instead of the small chip button. */
  overlay?: boolean;
}) {
  const [status, setStatus] = useState<TaskStatus | null>(null);
  const [loading, setLoading] = useState(false);
  const [, forceUpdate] = useState(0);
  const [hovered, setHovered] = useState(false);

  useEffect(() => onLocaleChange(() => forceUpdate((n) => n + 1)), []);

  useEffect(() => {
    if (!postUrl) return;
    let cancelled = false;
    getStatusBatcher(adapter.source)
      .enqueue(postUrl)
      .then((s) => { if (!cancelled) setStatus(s ?? TaskStatus.None); })
      .catch(() => { if (!cancelled) setStatus(TaskStatus.None); });
    return () => { cancelled = true; };
  }, [postUrl, adapter.source]);

  const handleAdd = () => {
    setLoading(true);
    const targets = adapter.targets ?? [1];
    httpRequest({
      method: 'POST',
      url: `${getApiBaseUrl()}/post-parser/task`,
      data: { sourceLinksMap: { [adapter.source]: [postUrl] }, targets },
      onSuccess: () => {
        showToast(t('addedToParseQueue'));
        setStatus(TaskStatus.Pending);
        setLoading(false);
      },
      onError: () => {
        alert(t('requestFailed'));
        setLoading(false);
      },
    });
  };

  const handleRemove = () => {
    setLoading(true);
    httpRequest({
      method: 'DELETE',
      url: `${getApiBaseUrl()}/post-parser/task/by-links`,
      data: { source: adapter.source, links: [postUrl] },
      onSuccess: () => {
        showToast(t('removedFromParseQueue'));
        setStatus(TaskStatus.Deleted);
        setLoading(false);
      },
      onError: () => {
        alert(t('requestFailed'));
        setLoading(false);
      },
    });
  };

  if (status === null) return null;

  const { label, action, icon: Icon, color, tone } = getButtonSpec(status);
  const handleClick = action === 'remove' ? handleRemove : handleAdd;

  if (overlay) {
    return (
      <CoverActionOverlay
        label={label}
        icon={Icon}
        tone={tone}
        busy={loading}
        href={postUrl || undefined}
        onActivate={handleClick}
      />
    );
  }

  return (
    <Tooltip content={label} placement="top" size="sm" color="foreground" portalContainer={getOverlayRoot()}>
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
