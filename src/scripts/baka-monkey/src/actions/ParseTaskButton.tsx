import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import { Tooltip } from '@heroui/tooltip';
import { MdOutlineDocumentScanner, MdOutlineDownloading, MdOutlineRefresh } from 'react-icons/md';
import type { ParseTaskAdapter } from '../types';
import { getApiBaseUrl, httpRequest } from '../api';
import { showToast } from '../components/Toast';
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

function getButtonProps(status: TaskStatus | null) {
  if (status === null || status === TaskStatus.None) {
    return { label: t('extractDownloadNow'), disabled: false, icon: MdOutlineDocumentScanner, color: 'primary' as const };
  }
  if (status === TaskStatus.Pending) {
    return { label: t('extractingDownload'), disabled: true, icon: MdOutlineDownloading, color: 'default' as const };
  }
  return { label: t('reExtractDownload'), disabled: false, icon: MdOutlineRefresh, color: 'secondary' as const };
}

export function ParseTaskButton({ adapter, postUrl }: { adapter: ParseTaskAdapter; postUrl: string }) {
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

  const handleClick = () => {
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

  if (status === null) return null;

  const { label, disabled, icon: Icon, color } = getButtonProps(status);
  const isClickable = !disabled && !loading;

  return (
    <Tooltip content={label} placement="top" size="sm" color="foreground">
      <Button
        size="sm"
        color={color}
        variant={isClickable && hovered ? 'solid' : 'flat'}
        isIconOnly
        isDisabled={!isClickable}
        onPress={handleClick}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      >
        <Icon size="1.2em" />
      </Button>
    </Tooltip>
  );
}
