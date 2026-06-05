"use client";

import type { DownloadTask } from "@/core/models/DownloadTask";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineWarning } from "react-icons/ai";

import {
  Alert,
  Checkbox,
  Chip,
  Modal,
  NumberInput,
  Textarea,
  toast,
} from "@/components/bakaui";
import { ThirdPartyId } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { useDownloadTasksStore } from "@/stores/downloadTasks";
import {
  DownloadTaskFieldMap,
  DownloadTaskFieldType,
} from "@/pages/downloader/components/TaskDetailModal/models";
import DownloadPathSelector from "@/pages/downloader/components/TaskDetailModal/components/DownloadPathSelector";

type Props = {
  tasks: DownloadTask[];
} & DestroyableProps;

/** The fields we allow to be edited in bulk. */
type EditableKey = "preferTorrent" | "autoRetry" | "checkpoint" | "interval" | "downloadPath";

type Values = {
  preferTorrent: boolean;
  autoRetry: boolean;
  checkpoint: string;
  interval?: number;
  downloadPath?: string;
};

const FieldTypeMap: Record<EditableKey, DownloadTaskFieldType> = {
  preferTorrent: DownloadTaskFieldType.PreferTorrent,
  autoRetry: DownloadTaskFieldType.AutoRetry,
  checkpoint: DownloadTaskFieldType.Checkpoint,
  interval: DownloadTaskFieldType.CheckInterval,
  downloadPath: DownloadTaskFieldType.DownloadPath,
};

const safeParseOptions = (o?: string): Record<string, any> => {
  if (!o) return {};
  try {
    return JSON.parse(o) ?? {};
  } catch {
    return {};
  }
};

// Existing tasks default to preferring torrents (matches the single-task modal).
const getPreferTorrent = (task: DownloadTask): boolean =>
  safeParseOptions(task.options).preferTorrent ?? true;

const allEqual = <T,>(values: T[]): boolean => values.every((v) => v === values[0]);

const BatchEditModal = ({ tasks, onDestroyed }: Props) => {
  const { t } = useTranslation();

  // A field is offered only when every selected task supports it — so the
  // universal fields always show, while PreferTorrent appears only when the
  // whole selection is ExHentai (the one type that declares it).
  const showField = (key: EditableKey) =>
    tasks.length > 0 &&
    tasks.every((task) =>
      (DownloadTaskFieldMap[task.thirdPartyId]?.[task.type] ?? []).some(
        (f) => f.type === FieldTypeMap[key],
      ),
    );

  const show: Record<EditableKey, boolean> = {
    preferTorrent: showField("preferTorrent"),
    autoRetry: showField("autoRetry"),
    checkpoint: showField("checkpoint"),
    interval: showField("interval"),
    downloadPath: showField("downloadPath"),
  };

  // Per-field current values across the selection, used to detect "mixed" state.
  const preferTorrents = tasks.map(getPreferTorrent);
  const autoRetries = tasks.map((x) => x.autoRetry);
  const checkpoints = tasks.map((x) => x.checkpoint ?? "");
  const intervals = tasks.map((x) => x.interval);
  const downloadPaths = tasks.map((x) => x.downloadPath ?? "");

  const mixed: Record<EditableKey, boolean> = {
    preferTorrent: !allEqual(preferTorrents),
    autoRetry: !allEqual(autoRetries),
    checkpoint: !allEqual(checkpoints),
    interval: !allEqual(intervals),
    downloadPath: !allEqual(downloadPaths),
  };

  const [edited, setEdited] = useState<Set<EditableKey>>(() => new Set());
  const [values, setValues] = useState<Values>(() => ({
    preferTorrent: mixed.preferTorrent ? false : preferTorrents[0] ?? true,
    autoRetry: mixed.autoRetry ? false : autoRetries[0] ?? false,
    checkpoint: mixed.checkpoint ? "" : checkpoints[0] ?? "",
    interval: mixed.interval ? undefined : intervals[0],
    downloadPath: mixed.downloadPath ? undefined : downloadPaths[0] || undefined,
  }));

  const isEdited = (k: EditableKey) => edited.has(k);

  const updateField = <K extends keyof Values>(key: K, value: Values[K]) => {
    setEdited((prev) => {
      if (prev.has(key)) {
        return prev;
      }
      const next = new Set(prev);

      next.add(key);

      return next;
    });
    setValues((prev) => ({ ...prev, [key]: value }) as Values);
  };

  const multiPlaceholder = t<string>("downloader.batchEdit.multipleValuesPlaceholder");

  const renderEditedChip = (k: EditableKey) =>
    isEdited(k) ? (
      <Chip
        color="warning"
        size="sm"
        startContent={<AiOutlineWarning className="text-sm" />}
        variant="flat"
      >
        {t<string>("downloader.batchEdit.edited")}
      </Chip>
    ) : null;

  const allSameThirdParty = allEqual(tasks.map((x) => x.thirdPartyId));
  const headerThirdPartyId = allSameThirdParty ? tasks[0]?.thirdPartyId : undefined;

  const save = async () => {
    if (edited.size === 0) {
      return;
    }

    // Baseline each task from the freshest store snapshot so untouched fields
    // (e.g. a checkpoint that advanced while a download was running) aren't
    // reverted to whatever the values were when the modal opened.
    const latestById = new Map(
      useDownloadTasksStore.getState().tasks.map((tk) => [tk.id, tk] as const),
    );

    const results = await Promise.all(
      tasks.map(async (task) => {
        const cur = latestById.get(task.id) ?? task;

        const options = isEdited("preferTorrent")
          ? JSON.stringify({
              ...safeParseOptions(cur.options),
              preferTorrent: values.preferTorrent,
            })
          : cur.options;

        // Each task keeps its own values for the fields the user didn't touch;
        // only the edited fields are overwritten.
        // NOTE: `downloadPath` isn't in the generated put model yet (the SDK can't
        // be regenerated offline in this environment); the backend already reads
        // it. Regenerating the SDK will make this typed and drop the cast.
        const payload = {
          interval: isEdited("interval") ? values.interval : cur.interval,
          startPage: cur.startPage,
          endPage: cur.endPage,
          checkpoint: isEdited("checkpoint") ? values.checkpoint || undefined : cur.checkpoint,
          autoRetry: isEdited("autoRetry") ? values.autoRetry : cur.autoRetry,
          options,
          downloadPath: isEdited("downloadPath") ? values.downloadPath : cur.downloadPath,
        };

        try {
          const r = await BApi.downloadTask.putDownloadTask(task.id, payload as any, {
            showErrorToast: () => false,
          });

          return !r.code;
        } catch {
          return false;
        }
      }),
    );

    const failed = results.filter((ok) => !ok).length;

    if (failed > 0) {
      // Throwing keeps the modal open (the bakaui Modal surfaces the message as a
      // toast) so the user can retry without re-entering their edits.
      throw new Error(t<string>("downloader.batchEdit.partialFailure", { count: failed }));
    }

    toast.success(t<string>("downloader.batchEdit.success", { count: tasks.length }));
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: { isDisabled: edited.size === 0 },
      }}
      size="xl"
      title={t<string>("downloader.batchEdit.title", { count: tasks.length })}
      onDestroyed={onDestroyed}
      onOk={save}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-2 text-sm text-default-500">
          {headerThirdPartyId != undefined ? (
            <span className="flex items-center gap-1">
              <ThirdPartyIcon thirdPartyId={headerThirdPartyId} />
              {ThirdPartyId[headerThirdPartyId]}
            </span>
          ) : (
            <span>{t<string>("downloader.batchEdit.multipleSources")}</span>
          )}
          <span>·</span>
          <span>{t<string>("downloader.batchEdit.selectedCount", { count: tasks.length })}</span>
        </div>

        <Alert
          color="warning"
          description={t<string>("downloader.batchEdit.overwriteWarning")}
          variant="flat"
        />

        {show.preferTorrent && (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <Checkbox
                isIndeterminate={!isEdited("preferTorrent") && mixed.preferTorrent}
                isSelected={values.preferTorrent}
                size="sm"
                onValueChange={(v) => updateField("preferTorrent", v)}
              >
                {t<string>("downloader.label.preferTorrent")}
              </Checkbox>
              {renderEditedChip("preferTorrent")}
            </div>
            <div className="text-xs text-default-400">
              {t<string>("downloader.tip.preferTorrentDesc")}
            </div>
          </div>
        )}

        {show.autoRetry && (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <Checkbox
                isIndeterminate={!isEdited("autoRetry") && mixed.autoRetry}
                isSelected={values.autoRetry}
                size="sm"
                onValueChange={(v) => updateField("autoRetry", v)}
              >
                {t<string>("downloader.label.autoRetry")}
              </Checkbox>
              {renderEditedChip("autoRetry")}
            </div>
            <div className="text-xs text-default-400">
              {t<string>("downloader.tip.autoRetryDesc")}
            </div>
          </div>
        )}

        {show.checkpoint && (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <span className="text-sm">{t<string>("common.label.checkpoint")}</span>
              {renderEditedChip("checkpoint")}
            </div>
            <Textarea
              placeholder={
                mixed.checkpoint && !isEdited("checkpoint") ? multiPlaceholder : undefined
              }
              size="sm"
              value={values.checkpoint}
              onValueChange={(v) => updateField("checkpoint", v)}
            />
          </div>
        )}

        {show.interval && (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <span className="text-sm">{t<string>("downloader.label.checkInterval")}</span>
              {renderEditedChip("interval")}
            </div>
            <NumberInput
              placeholder={mixed.interval && !isEdited("interval") ? multiPlaceholder : undefined}
              value={values.interval}
              onValueChange={(v) => updateField("interval", Number.isNaN(v) ? undefined : v)}
            />
            <div className="text-xs text-default-400">
              {t<string>("downloader.tip.checkIntervalDesc")}
            </div>
          </div>
        )}

        {show.downloadPath && (
          <div className="flex items-center gap-2">
            <DownloadPathSelector
              downloadPath={
                isEdited("downloadPath")
                  ? values.downloadPath
                  : mixed.downloadPath
                    ? undefined
                    : downloadPaths[0] || undefined
              }
              placeholder={
                mixed.downloadPath && !isEdited("downloadPath")
                  ? t<string>("downloader.batchEdit.multipleValues")
                  : undefined
              }
              onChange={(p) => updateField("downloadPath", p)}
            />
            {renderEditedChip("downloadPath")}
          </div>
        )}
      </div>
    </Modal>
  );
};

BatchEditModal.displayName = "BatchEditModal";

export default BatchEditModal;
