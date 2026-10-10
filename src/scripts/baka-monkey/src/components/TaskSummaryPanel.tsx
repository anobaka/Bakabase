import { useEffect, useState } from 'react';
import { IoClose, IoOpenOutline } from 'react-icons/io5';
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

  return (
    <div
      data-task-summary={siteKey}
      style={{
        position: 'relative',
        width: 240,
        maxWidth: 'calc(100vw - 40px)',
        borderRadius: 12,
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
        title={t('taskSummary.openTasks')}
        aria-disabled={!taskPageUrl}
        style={{ display: 'block', padding: 14, cursor: taskPageUrl ? 'pointer' : 'default' }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, paddingRight: 24, fontSize: 13, fontWeight: 600 }}>
          <span>{title}</span>
          <IoOpenOutline aria-hidden="true" size={14} />
        </div>
        <div style={{ display: 'flex', gap: 12, marginTop: 12 }}>
          {items.map(({ label, value, color }) => (
            <div key={label} style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontSize: 20, lineHeight: 1.2, fontWeight: 700, fontVariantNumeric: 'tabular-nums', overflowWrap: 'anywhere', color }}>
                {value === undefined ? '—' : value.toLocaleString()}
              </div>
              <div style={{ marginTop: 4, fontSize: 11, color: '#71717a' }}>{label}</div>
            </div>
          ))}
        </div>
        {summary === null && (
          <div role="status" style={{ marginTop: 10, fontSize: 11, color: '#71717a' }}>
            {t('taskSummary.unavailable')}
          </div>
        )}
      </a>
      <button
        type="button"
        aria-label={t('taskSummary.close')}
        title={t('taskSummary.close')}
        onClick={onClose}
        style={{
          position: 'absolute',
          top: 8,
          right: 8,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 24,
          height: 24,
          borderRadius: 6,
          color: '#71717a',
          cursor: 'pointer',
        }}
      >
        <IoClose size={16} aria-hidden="true" />
      </button>
    </div>
  );
}
