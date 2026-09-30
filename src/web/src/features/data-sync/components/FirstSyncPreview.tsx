import type {
  DataSyncFirstSyncChoice,
  DataSyncFirstSyncPreview,
  DataSyncPreviewEntry,
} from "../api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import { isTaskOver, useDataSyncTask } from "../hooks/useDataSyncTask";
import { useCanManageDefinitionSharing } from "../hooks/useCanManageDefinitionSharing";
import { useDataSyncStore } from "../stores/dataSync";
import { timeAgo } from "../times";
import { sharingNeeded, twoWayConfirmation } from "../viewModels";

import DataSyncDialog from "./DataSyncDialog";
import InlineConfirmation from "./InlineConfirmation";
import { buttonClass, DataSyncErrorNotice, fieldClass, primaryClass } from "./common";

import {
  DataSyncFirstSyncAction,
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncPreviewOutcome,
  DataSyncPreviewOutcomeLabel,
  DataSyncProblemCodeLabel,
  RemoteAccessMode,
} from "@/sdk/constants";

/*
 * A first sync (spec §8.3): what the other device's definitions would do here — the ordinary
 * merge, previewed against this device's definitions as they are now — then Start, which runs it
 * as one task. Any definition can be left out; a copy once answers its name matches here, a link
 * asks them under "Needs you" after the Start. After a copy once, the reader chooses whether to
 * keep receiving.
 */

/** How often the preview is read again while its snapshot is fetched or its Start runs. */
export const PREVIEW_POLL_MS = 1_500;

/** The groups the preview lists, in order; what is already the same is only counted. */
const groups = [
  DataSyncPreviewOutcome.Create,
  DataSyncPreviewOutcome.Update,
  DataSyncPreviewOutcome.Delete,
  DataSyncPreviewOutcome.NameMatch,
  DataSyncPreviewOutcome.Question,
  DataSyncPreviewOutcome.Held,
  DataSyncPreviewOutcome.NotSynced,
];

/** What a person may leave out of the sync. */
const skippable = new Set([
  DataSyncPreviewOutcome.Create,
  DataSyncPreviewOutcome.Update,
  DataSyncPreviewOutcome.Delete,
  DataSyncPreviewOutcome.NameMatch,
  DataSyncPreviewOutcome.Question,
]);

const keyOf = (entry: DataSyncPreviewEntry) => `${entry.kind}/${entry.key}`;

type Phase =
  | "loading"
  | "access"
  | "preparing"
  | "preview"
  | "applying"
  | "failed"
  | "done"
  | "gone";

export interface FirstSyncPreviewProps {
  linkId: number;
  /** The device the first sync reads. */
  peerName: string;
  peerNodeId?: string;
  onClose: () => void;
  /** Where the keyboard goes on closing once the link that opened the preview is gone. */
  returnFocus?: () => HTMLElement | null | undefined;
  /** Something changed here: the page reads data sync again. */
  onChanged: () => void;
  now?: number;
}

export default function FirstSyncPreview({
  linkId,
  peerName,
  peerNodeId,
  onClose,
  returnFocus,
  onChanged,
  now,
}: FirstSyncPreviewProps) {
  const { t } = useTranslation();
  const [preview, setPreview] = useState<DataSyncFirstSyncPreview>();
  const [loadError, setLoadError] = useState<Error>();
  const [choices, setChoices] = useState<Map<string, DataSyncFirstSyncChoice>>(new Map());
  const [taskId, setTaskId] = useState<string>();
  /** Started here: what the Start left under "Needs you", once it is done. */
  const [started, setStarted] = useState<{ asked: number }>();
  const actions = useDataSyncActions(() => undefined);
  const task = useDataSyncTask(taskId ?? preview?.taskId);
  const name = preview?.source?.name || peerName;
  const copyOnce = preview?.copyOnce ?? false;

  const load = useCallback(async () => {
    try {
      setPreview(await dataSyncApi.firstSync(linkId));
      setLoadError(undefined);
    } catch (cause) {
      setLoadError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, [linkId]);

  const phase: Phase = !preview
    ? "loading"
    : preview.problem
      ? "gone"
      : task && !isTaskOver(task.phase)
        ? "applying"
        : preview.state !== DataSyncLinkState.AwaitingReview
          ? started
            ? "done"
            : preview.state === DataSyncLinkState.AwaitingAccess
              ? "access"
              : "gone"
          : !preview.source
            ? "preparing"
            : task && task.phase !== "completed"
              ? "failed"
              : "preview";

  useEffect(() => {
    void load();
  }, [load]);

  // Read again while the snapshot is fetched or the Start runs, and at once when it is over.
  useEffect(() => {
    if (phase !== "preparing" && phase !== "applying") return;
    const timer = setInterval(() => void load(), PREVIEW_POLL_MS);

    return () => clearInterval(timer);
  }, [phase, load]);
  useEffect(() => {
    if (task && isTaskOver(task.phase)) void load();
  }, [task?.phase, load]);
  useEffect(() => {
    if (phase === "done") onChanged();
  }, [phase === "done"]);

  const entries = preview?.entries ?? [];
  const choose = (
    entry: DataSyncPreviewEntry,
    choice?: Omit<DataSyncFirstSyncChoice, "kind" | "key">,
  ) =>
    setChoices((current) => {
      const next = new Map(current);

      if (choice) next.set(keyOf(entry), { kind: entry.kind, key: entry.key, ...choice });
      else next.delete(keyOf(entry));

      return next;
    });

  const start = () =>
    void actions.run(async () => {
      // A copy once leaves out what the person left out, so a name match this preview did not show
      // makes the Start apply nothing (§8.3).
      const leftOut = copyOnce
        ? entries
            .filter((e) => e.outcome === DataSyncPreviewOutcome.NameMatch && !choices.has(keyOf(e)))
            .map(({ kind, key }) => ({ kind, key, action: DataSyncFirstSyncAction.Skip }))
        : [];
      const answer = await dataSyncApi.startFirstSync(linkId, [...choices.values(), ...leftOut]);

      setStarted({
        asked: copyOnce
          ? 0
          : entries.filter(
              (entry) =>
                !choices.has(keyOf(entry)) &&
                (entry.outcome === DataSyncPreviewOutcome.NameMatch ||
                  entry.outcome === DataSyncPreviewOutcome.Question),
            ).length,
      });
      setTaskId(answer.taskId ?? undefined);
    }, []);

  return (
    <DataSyncDialog
      wide
      busy={actions.busy}
      footer={
        phase === "preview" || phase === "failed" ? (
          <div className="flex w-full flex-wrap items-center justify-between gap-2">
            <p className="text-xs text-default-500">{t("dataSync.review.safe")}</p>
            <div className="flex gap-2">
              <button
                className={buttonClass}
                disabled={actions.busy}
                type="button"
                onClick={onClose}
              >
                {t("dataSync.review.later")}
              </button>
              <button
                className={primaryClass}
                data-testid="data-sync-review-start"
                disabled={actions.busy}
                type="button"
                onClick={start}
              >
                {t("dataSync.review.start")}
              </button>
            </div>
          </div>
        ) : (
          <button className={buttonClass} type="button" onClick={onClose}>
            {phase === "done" ? t("dataSync.done") : t("dataSync.close")}
          </button>
        )
      }
      returnFocus={returnFocus}
      testId="data-sync-review"
      title={
        copyOnce ? t("dataSync.review.copyTitle", { name }) : t("dataSync.review.title", { name })
      }
      onClose={onClose}
    >
      <DataSyncErrorNotice error={loadError} onRetry={() => void load()} />
      <DataSyncErrorNotice error={actions.error} onDismiss={() => actions.setError(undefined)} />
      {phase === "loading" && !loadError && (
        <p className="text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      {phase === "access" && (
        <div className="space-y-1 text-sm" data-testid="data-sync-review-awaiting-access">
          <p role="status">{t("dataSync.status.AwaitingAccess", { name })}</p>
          <p className="text-xs text-default-500">{t("dataSync.link.approveThere", { name })}</p>
        </div>
      )}
      {phase === "preparing" && (
        <p className="text-sm" data-testid="data-sync-review-preparing" role="status">
          {t("dataSync.wizard.fetching", { name })}
        </p>
      )}
      {phase === "gone" && (
        <p className="text-sm" data-testid="data-sync-review-gone">
          {t(
            `dataSync.problem.${
              (preview?.problem && DataSyncProblemCodeLabel[preview.problem.code]) ||
              "NothingToReview"
            }`,
          )}
        </p>
      )}
      {(phase === "preview" || phase === "failed") && preview?.source && (
        <>
          <p className="text-xs text-default-500" data-testid="data-sync-review-fetched">
            {t("dataSync.review.fetched", { time: timeAgo(t, preview.source.fetchedAt, now) })}
            {preview.mode === DataSyncLinkMode.TwoWay &&
              ` · ${t("dataSync.review.twoWay", { name })}`}
          </p>
          {phase === "failed" && (
            <p
              className="text-sm text-danger-700"
              data-testid="data-sync-review-failed"
              role="alert"
            >
              {t("dataSync.review.failed", { reason: task?.error || "" })}
            </p>
          )}
          <PreviewGroups
            choices={choices}
            copyOnce={copyOnce}
            disabled={actions.busy}
            entries={entries}
            name={name}
            onChoose={choose}
          />
        </>
      )}
      {phase === "applying" && (
        <div className="space-y-2" data-testid="data-sync-review-applying" role="status">
          <p className="text-sm">
            {task?.phase === "running"
              ? t("dataSync.review.applying")
              : task?.waitingReason
                ? t("dataSync.review.waitingFor", { reason: task.waitingReason })
                : t("dataSync.review.waiting")}
          </p>
          <p className="text-xs text-default-500">{t("dataSync.review.closeWhileApplying")}</p>
        </div>
      )}
      {phase === "done" && (
        <div className="space-y-3" data-testid="data-sync-review-done">
          <p className="text-sm font-medium">
            {copyOnce
              ? t("dataSync.review.copied", { name })
              : t("dataSync.review.inStep", { name })}
          </p>
          {!!started?.asked && (
            <p className="text-xs text-default-500">
              {t("dataSync.review.askedAfter", { count: started.asked })}
            </p>
          )}
          {copyOnce && (
            <CopyOnceFollowUp
              linkId={linkId}
              name={name}
              peerNodeId={preview?.source?.nodeId ?? peerNodeId}
              onChanged={onChanged}
              onClose={onClose}
            />
          )}
        </div>
      )}
    </DataSyncDialog>
  );
}

/** Every group of the preview with its definitions, and what the person may choose for each. */
function PreviewGroups({
  entries,
  choices,
  copyOnce,
  name,
  disabled,
  onChoose,
}: {
  entries: DataSyncPreviewEntry[];
  choices: Map<string, DataSyncFirstSyncChoice>;
  copyOnce: boolean;
  name: string;
  disabled: boolean;
  onChoose: (
    entry: DataSyncPreviewEntry,
    choice?: Omit<DataSyncFirstSyncChoice, "kind" | "key">,
  ) => void;
}) {
  const { t } = useTranslation();
  const same = entries.filter((entry) => entry.outcome === DataSyncPreviewOutcome.Unchanged).length;
  const shown = groups.filter((outcome) => entries.some((entry) => entry.outcome === outcome));

  if (shown.length === 0)
    return (
      <p className="text-sm" data-testid="data-sync-review-nothing">
        {t("dataSync.review.nothing", { name })}
      </p>
    );

  return (
    <div className="space-y-3">
      {shown.map((outcome) => {
        const label = DataSyncPreviewOutcomeLabel[outcome];
        const mine = entries.filter((entry) => entry.outcome === outcome);
        const asks = outcome === DataSyncPreviewOutcome.NameMatch && copyOnce;

        return (
          <section key={outcome} data-group={label} data-testid="data-sync-review-group">
            <h3 className="text-sm font-medium">
              {t(`dataSync.review.group.${label}`)} ({mine.length})
            </h3>
            {(outcome === DataSyncPreviewOutcome.NameMatch ||
              outcome === DataSyncPreviewOutcome.Question) && (
              <p className="text-xs text-default-500">
                {t(`dataSync.review.hint.${copyOnce ? `copy${label}` : label}`, { name })}
              </p>
            )}
            <ul className="divide-y divide-default-100">
              {mine.map((entry) => {
                const choice = choices.get(keyOf(entry));

                return (
                  <li
                    key={keyOf(entry)}
                    className="flex flex-wrap items-center justify-between gap-2 py-1 text-sm"
                    data-testid="data-sync-review-entry"
                  >
                    <span className="min-w-0 break-words">
                      <span className="font-medium">{entry.name}</span>{" "}
                      <span className="text-xs text-default-500">
                        {t(`dataSync.kind.${entry.kind}`)}
                        {entry.subtype && ` · ${entry.subtype}`}
                      </span>
                    </span>
                    {asks ? (
                      <select
                        aria-label={t("dataSync.review.choice.label", { entity: entry.name })}
                        className={fieldClass}
                        disabled={disabled}
                        value={choice?.localKey ?? (choice ? "keepBoth" : "")}
                        onChange={(event) => {
                          const value = event.target.value;

                          onChoose(
                            entry,
                            value === ""
                              ? undefined
                              : value === "keepBoth"
                                ? { action: DataSyncFirstSyncAction.KeepBoth }
                                : { action: DataSyncFirstSyncAction.Link, localKey: value },
                          );
                        }}
                      >
                        <option value="">{t("dataSync.review.choice.leaveOut")}</option>
                        {(entry.candidates ?? [])
                          .filter((candidate) => candidate.updatable)
                          .map((candidate) => (
                            <option key={candidate.localKey} value={candidate.localKey}>
                              {t("dataSync.review.choice.link", { target: candidate.name })}
                            </option>
                          ))}
                        <option value="keepBoth">{t("dataSync.review.choice.keepBoth")}</option>
                      </select>
                    ) : (
                      skippable.has(outcome) && (
                        <label className="flex items-center gap-1 text-xs">
                          <input
                            checked={choice?.action === DataSyncFirstSyncAction.Skip}
                            data-testid="data-sync-review-skip"
                            disabled={disabled}
                            type="checkbox"
                            onChange={(event) =>
                              onChoose(
                                entry,
                                event.target.checked
                                  ? { action: DataSyncFirstSyncAction.Skip }
                                  : undefined,
                              )
                            }
                          />
                          {t("dataSync.review.skip")}
                        </label>
                      )
                    )}
                  </li>
                );
              })}
            </ul>
          </section>
        );
      })}
      {same > 0 && (
        <p className="text-xs text-default-500" data-testid="data-sync-review-same">
          {t("dataSync.review.same", { count: same })}
        </p>
      )}
    </div>
  );
}

/** After a copy once: keep receiving, keep in step both ways, or stop reading the other device. */
function CopyOnceFollowUp({
  linkId,
  name,
  peerNodeId,
  onChanged,
  onClose,
}: {
  linkId: number;
  name: string;
  peerNodeId?: string;
  onChanged: () => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const actions = useDataSyncActions(() => undefined);
  const overview = useDataSyncStore((state) => state.overview);
  // The server's word on who may create access counts too: the window's own guess keeps its
  // defaults — this device's own window — when who is looking could not be read.
  const canManage = useCanManageDefinitionSharing() && (overview?.canManageSharing ?? true);
  const keepInStepButton = useRef<HTMLButtonElement>(null);
  const own = {
    sharingEnabled: overview?.sharingEnabled ?? false,
    remoteAccessMode: overview?.remoteAccessMode ?? RemoteAccessMode.Disabled,
  };
  const mustTurnOn = sharingNeeded(own);

  const then = (operation: () => Promise<unknown>) =>
    void actions.run(async () => {
      await operation();
      onChanged();
      onClose();
    }, []);

  /**
   * Keeping in step both ways lets the other device read this one: asked first, in the words
   * every other place that offers it uses — and saying what it turns on here, only if it is off.
   */
  const keepInStep = () =>
    actions.confirm({
      ...twoWayConfirmation(t, name, own, mustTurnOn),
      action: async () => {
        if (mustTurnOn)
          await dataSyncApi.setSharing({
            enabled: true,
            enablePairedRemoteAccess: own.remoteAccessMode === RemoteAccessMode.Disabled,
          });
        await dataSyncApi.updateLink(linkId, { mode: DataSyncLinkMode.TwoWay });
        onChanged();
        onClose();
      },
      refresh: [],
    });

  return (
    <div className="space-y-2" data-testid="data-sync-review-follow-up">
      <DataSyncErrorNotice error={actions.error} onDismiss={() => actions.setError(undefined)} />
      <div className="flex flex-wrap gap-2">
        <button
          className={buttonClass}
          data-testid="data-sync-review-keep-receiving"
          disabled={actions.busy}
          type="button"
          onClick={() =>
            then(() => dataSyncApi.updateLink(linkId, { mode: DataSyncLinkMode.Follow }))
          }
        >
          {t("dataSync.review.keepReceiving", { name })}
        </button>
        {canManage && (
          <button
            ref={keepInStepButton}
            aria-expanded={!!actions.confirmation}
            className={buttonClass}
            data-testid="data-sync-review-keep-in-step"
            disabled={actions.busy}
            type="button"
            onClick={keepInStep}
          >
            {t("dataSync.mode.twoWay")}
          </button>
        )}
        {peerNodeId && (
          <button
            className={buttonClass}
            data-testid="data-sync-review-stop-reading"
            disabled={actions.busy}
            type="button"
            onClick={() => then(() => dataSyncApi.forgetAccess(peerNodeId))}
          >
            {t("dataSync.link.stopReading.button", { name })}
          </button>
        )}
      </div>
      {actions.confirmation && (
        <InlineConfirmation
          busy={actions.busy}
          confirmation={actions.confirmation}
          error={actions.confirmationError}
          onCancel={() => {
            actions.cancelConfirmation();
            keepInStepButton.current?.focus();
          }}
          onConfirm={actions.confirmCurrent}
        />
      )}
    </div>
  );
}
