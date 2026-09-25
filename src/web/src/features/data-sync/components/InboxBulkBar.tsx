import type { BackupTarget } from "../hooks/useBackupTarget";
import type { InboxBulk } from "../inboxModels";

import { useTranslation } from "react-i18next";

import { BackupCheckbox } from "./InboxCard";
import { smallButtonClass } from "./common";

/*
 * Deciding many at once (spec §9.1): link every exact match, skip every suggestion, delete or
 * keep every deletion, keep this device's or take one device's for every conflict. Each sends
 * one batch, backed up once.
 */

export default function InboxBulkBar({
  bulks,
  busy,
  backup,
  backupFirst,
  onBackupFirst,
  onRun,
}: {
  bulks: InboxBulk[];
  busy: boolean;
  backup: BackupTarget;
  backupFirst: boolean;
  onBackupFirst: (checked: boolean) => void;
  onRun: (bulk: InboxBulk) => void;
}) {
  const { t } = useTranslation();

  if (!bulks.length) return null;

  return (
    <div className="space-y-2 rounded-lg bg-default-50 p-2" data-testid="data-sync-inbox-bulk">
      <div className="flex flex-wrap gap-2">
        {bulks.map((bulk) => (
          <button
            key={`${bulk.id}-${bulk.peer?.nodeId ?? ""}`}
            className={`${smallButtonClass} ${bulk.destructive ? "border-danger/40 text-danger" : ""}`}
            data-bulk={bulk.id}
            data-testid="data-sync-inbox-bulk-action"
            disabled={busy}
            type="button"
            onClick={() => onRun(bulk)}
          >
            {t(`dataSync.inbox.bulk.${bulk.id}`, {
              count: bulk.count,
              name: bulk.peer?.name ?? "",
            })}
          </button>
        ))}
      </div>
      {bulks.some((bulk) => bulk.destructive) && (
        <BackupCheckbox
          backup={backup}
          checked={backupFirst}
          disabled={busy}
          onChange={onBackupFirst}
        />
      )}
    </div>
  );
}
