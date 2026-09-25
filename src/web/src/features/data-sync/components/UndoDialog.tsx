import type { DataSyncHistoryEntry, DataSyncUndoPreview, DataSyncUndoPreviewItem } from "../api";
import type { UndoGroup } from "../historyModels";
import type { DataSyncUndoBlock } from "@/sdk/constants";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi, throwIfProblem } from "../api";
import { useBackupTarget } from "../hooks/useBackupTarget";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import { historyKindName, undoGroups } from "../historyModels";

import DataSyncDialog from "./DataSyncDialog";
import { buttonClass, DataSyncErrorNotice, primaryClass } from "./common";

import {
  DataSyncLinkMode,
  DataSyncProblemCodeLabel,
  DataSyncUndoBlockLabel,
} from "@/sdk/constants";

/*
 * What undoing one history entry would do (spec §8.11), grouped the way it happens: definitions
 * removed, reverted, re-created, unlinked, kept apart — and those it has to keep, with why. It
 * says what undo cannot bring back, where the backup goes, and how long history is kept.
 */

export interface UndoDialogProps {
  entry: DataSyncHistoryEntry;
  /** The mode of the link the entry came through, if any: undoing a change syncs out. */
  linkMode?: DataSyncLinkMode;
  onClose: () => void;
  /** The undo was started, as the task it runs as. */
  onStarted: (taskId?: string) => void;
}

const blockKey = (block: DataSyncUndoBlock) =>
  DataSyncUndoBlockLabel[block] ?? "ChangedSinceImport";

export default function UndoDialog({ entry, linkMode, onClose, onStarted }: UndoDialogProps) {
  const { t } = useTranslation();
  const [preview, setPreview] = useState<DataSyncUndoPreview>();
  const [loadError, setLoadError] = useState<Error>();
  const actions = useDataSyncActions(() => undefined);
  const backup = useBackupTarget();
  const name = entry.peerName ?? "";

  useEffect(() => {
    let live = true;

    dataSyncApi.undoPreview(entry.id).then(
      (answer) => live && setPreview(answer ?? undefined),
      (cause) => live && setLoadError(cause instanceof Error ? cause : new Error(String(cause))),
    );

    return () => {
      live = false;
    };
  }, [entry.id]);

  const groups = preview
    ? undoGroups(preview.items)
    : new Map<UndoGroup, DataSyncUndoPreviewItem[]>();
  const note = (group: UndoGroup) => {
    switch (group) {
      case "remove":
        return name ? t("dataSync.undo.note.remove", { name }) : undefined;
      case "unlink":
        return name ? t("dataSync.undo.note.unlink", { name }) : undefined;
      case "revert":
        return linkMode === DataSyncLinkMode.Follow && name
          ? t("dataSync.undo.note.revertFollow", { name })
          : t("dataSync.undo.note.revert");
      case "recreate":
        return t("dataSync.undo.note.recreate");
      case "exclude":
        return name ? t("dataSync.undo.note.exclude", { name }) : undefined;
      default:
        return t("dataSync.undo.note.keep");
    }
  };

  const undo = () =>
    void actions.run(async () => {
      const start = throwIfProblem(await dataSyncApi.undo(entry.id));

      onStarted(start.taskId ?? undefined);
    }, []);

  return (
    <DataSyncDialog
      wide
      busy={actions.busy}
      footer={
        <>
          <button className={buttonClass} disabled={actions.busy} type="button" onClick={onClose}>
            {t("dataSync.cancel")}
          </button>
          <button
            className={primaryClass}
            data-testid="data-sync-undo-confirm"
            disabled={actions.busy || !preview?.canUndo}
            type="button"
            onClick={undo}
          >
            {t("dataSync.undo.button")}
          </button>
        </>
      }
      testId="data-sync-undo"
      title={t("dataSync.undo.title", {
        kind: t(`dataSync.history.kind.${historyKindName(entry.kind)}`),
      })}
      onClose={onClose}
    >
      <DataSyncErrorNotice error={loadError} />
      <DataSyncErrorNotice error={actions.error} onDismiss={() => actions.setError(undefined)} />
      {!preview && !loadError && (
        <p className="text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      {preview?.problem && (
        <p className="text-sm text-danger-700" role="alert">
          {t(
            `dataSync.problem.${DataSyncProblemCodeLabel[preview.problem.code] ?? "UndoNotAvailable"}`,
          )}
        </p>
      )}
      {preview && !preview.problem && groups.size === 0 && (
        <p className="text-sm">{t("dataSync.undo.nothing")}</p>
      )}
      {Array.from(groups, ([group, items]) => (
        <section
          key={group}
          className="space-y-1 rounded-lg border border-default-200 p-2"
          data-group={group}
          data-testid="data-sync-undo-group"
        >
          <h3 className="text-sm font-medium">
            {t(`dataSync.undo.group.${group}`, { count: items.length, name })}
          </h3>
          {note(group) && <p className="text-xs text-default-500">{note(group)}</p>}
          <ul className="space-y-0.5 text-xs">
            {items.map((item) => (
              <li key={`${item.kind}-${item.localKey}`} data-local-key={item.localKey}>
                <span className="font-medium">{item.name}</span>
                {item.blocked != null && (
                  <span className="text-default-500">
                    {" "}
                    ·{" "}
                    {t(`dataSync.undo.blocked.${blockKey(item.blocked)}`, {
                      count: item.valueCount ?? 0,
                    })}
                  </span>
                )}
                {item.blocked == null && item.valueCount != null && item.valueCount > 0 && (
                  <span className="text-default-500">
                    {" "}
                    · {t("dataSync.undo.values", { count: item.valueCount })}
                  </span>
                )}
                {item.settingsMayReferenceIt && (
                  <span className="block text-warning-700 dark:text-warning">
                    {t("dataSync.undo.settingsLoseIt")}
                  </span>
                )}
                {item.recreatedGetsNewId && (
                  <span className="block text-default-500">{t("dataSync.undo.newId")}</span>
                )}
              </li>
            ))}
          </ul>
        </section>
      ))}
      {entry.counts.typeChanged > 0 && (
        <p className="text-xs text-default-500">{t("dataSync.undo.typeChangeLost")}</p>
      )}
      <div className="space-y-1 text-xs text-default-500">
        <p data-testid="data-sync-undo-backup">
          {backup.folder
            ? t("dataSync.undo.backup", { folder: backup.folder })
            : t("dataSync.undo.backupNoFolder")}
        </p>
        <p>{t("dataSync.undo.retention")}</p>
      </div>
    </DataSyncDialog>
  );
}
