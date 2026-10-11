import { useEffect, useState } from 'react';
import { IoClose } from 'react-icons/io5';
import { getApiBaseUrl } from '../api';
import type { ConnectionState } from '../heartbeat';
import { onLocaleChange, t } from '../i18n';
import { normalizeApiBaseUrl } from '../requests';
import {
  getTaskPageUrl,
  startTaskSummaryPolling,
  type TaskSummary,
  type TaskSummaryTarget,
} from '../taskSummary';

export function TaskSummaryPanel({
  siteKey,
  target,
  connection,
  onClose,
}: {
  siteKey: string;
  target: TaskSummaryTarget;
  connection: ConnectionState;
  onClose: () => void;
}) {
  const [summary, setSummary] = useState<TaskSummary | null>(null);
  const [, forceUpdate] = useState(0);

  useEffect(() => onLocaleChange(() => forceUpdate((value) => value + 1)), []);

  useEffect(() => {
    let stopPolling: (() => void) | undefined;
    const syncVisibility = () => {
      stopPolling?.();
      stopPolling = undefined;
      setSummary(null);
      if (connection.connected && document.visibilityState !== 'hidden') {
        stopPolling = startTaskSummaryPolling({ target, onUpdate: setSummary });
      }
    };

    syncVisibility();
    document.addEventListener('visibilitychange', syncVisibility);
    return () => {
      document.removeEventListener('visibilitychange', syncVisibility);
      stopPolling?.();
    };
  }, [target, connection.connected, connection.baseUrl]);

  let taskPageUrl: string | undefined;
  try {
    taskPageUrl = getTaskPageUrl(normalizeApiBaseUrl(connection.baseUrl || getApiBaseUrl()), target);
  } catch {
    // An invalid saved address still leaves the close and settings controls usable.
  }

  const title = t(target.kind === 'download' ? 'taskSummary.downloadTasks' : 'taskSummary.parseTasks');
  const items = [
    { label: t('taskSummary.completed'), value: summary?.completed, color: '#17a34a' },
    { label: t('taskSummary.failed'), value: summary?.failed, color: '#e11d48' },
    { label: t('taskSummary.total'), value: summary?.total, color: '#27272a' },
  ];
  const description = summary === null
    ? t('taskSummary.unavailable')
    : items.map(({ label, value }) => `${label}: ${value?.toLocaleString()}`).join(', ');

  return (
    <div
      data-task-summary={siteKey}
      style={{
        display: 'flex',
        alignItems: 'center',
        width: 'fit-content',
        maxWidth: 'calc(100vw - 40px)',
        borderRadius: 10,
        background: '#fff',
        border: '1px solid #e4e4e7',
        boxShadow: '0 4px 20px rgba(0,0,0,0.12)',
        color: '#27272a',
        fontFamily: 'system-ui, -apple-system, "Segoe UI", sans-serif',
      }}
    >
      <a
        href={taskPageUrl}
        target="_blank"
        rel="noopener noreferrer"
        aria-label={`${title}: ${description}`}
        aria-disabled={!taskPageUrl}
        style={{
          display: 'flex',
          gap: 12,
          minWidth: 0,
          padding: '8px 10px',
          textDecoration: 'none',
          cursor: taskPageUrl ? 'pointer' : 'default',
        }}
      >
        {items.map(({ label, value, color }) => (
          <span
            key={label}
            title={value === undefined ? `${label}: ${t('taskSummary.unavailable')}` : label}
            style={{ minWidth: 0, fontSize: 14, lineHeight: '20px', fontWeight: 700, fontVariantNumeric: 'tabular-nums', overflowWrap: 'anywhere', color }}
          >
            {value === undefined ? '—' : value.toLocaleString()}
          </span>
        ))}
      </a>
      <button
        type="button"
        aria-label={t('taskSummary.close')}
        title={t('taskSummary.close')}
        onClick={onClose}
        style={{
          display: 'flex',
          flexShrink: 0,
          alignItems: 'center',
          justifyContent: 'center',
          width: 24,
          height: 24,
          marginRight: 4,
          padding: 0,
          border: 0,
          borderRadius: 6,
          background: 'transparent',
          color: '#71717a',
          cursor: 'pointer',
        }}
      >
        <IoClose size={16} aria-hidden="true" />
      </button>
    </div>
  );
}
