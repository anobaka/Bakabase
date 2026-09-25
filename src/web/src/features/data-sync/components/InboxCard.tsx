import type { TFunction } from "i18next";
import type {
  DataSyncFieldOutcome,
  DataSyncInboxItemView,
  DataSyncResolveBatchInput,
  DataSyncTypeChangePreview,
} from "../api";
import type { BackupTarget } from "../hooks/useBackupTarget";
import type { ConflictChoices, InboxCardModel, InboxChoice } from "../inboxModels";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import {
  actionKey,
  batchOf,
  cardIsDestructive,
  closureKey,
  conflictBatch,
  conflictDecided,
  conflictFields,
  headlineKey,
  inboxChoices,
  isConflict,
  resolveInput,
} from "../inboxModels";
import { localDateTime, timeAgo } from "../times";

import { pathLabel } from "./ChangeList";
import DisplayValue, { displayText } from "./DisplayValue";
import { DataSyncErrorNotice, fieldClass, linkButtonClass, smallButtonClass } from "./common";

import {
  DataSyncInboxAction,
  DataSyncInboxItemType,
  DataSyncInboxItemTypeLabel,
  DataSyncNaturalMatchLabel,
} from "@/sdk/constants";

/*
 * One card of "Needs you" (spec §11.3): what happened and where on top, a small drawn comparison
 * — the last agreed value faint above this device's and the other device's — then one button per
 * action the item allows. Values read as people read them; ids never appear. Destructive actions
 * back the database up first unless the reader unticks it.
 */

/** A confirmation the card asks for before a destructive batch. */
export interface InboxConfirmation {
  title: string;
  description: string;
  warning?: string;
}

export interface InboxCardProps {
  card: InboxCardModel;
  busy: boolean;
  error?: Error;
  /** A decision was sent and runs. */
  applying?: boolean;
  /** Closed while it was shown: says how, then leaves. */
  closedItem?: DataSyncInboxItemView;
  backup: BackupTarget;
  onResolve: (batch: DataSyncResolveBatchInput, confirmation?: InboxConfirmation) => void;
  /** Pauses the link a large change came from (a link action, not an inbox action). */
  onPauseLink?: (linkId: number) => void;
  onDismissError?: () => void;
  now?: number;
}

const peerName = (item: DataSyncInboxItemView) =>
  item.peerName || item.payload.remoteEditor?.name || item.payload.peerName || "";

/** The values a card's words take. */
export const cardValues = (card: InboxCardModel) => {
  const first = card.items[0];
  const payload = first.payload;

  return {
    entity: payload.entityName,
    name: peerName(first),
    names: card.peers.map((peer) => peer.name).join(", ") || peerName(first),
    count:
      first.type === DataSyncInboxItemType.LargeChange
        ? payload.childrenTotal || payload.largeChange?.length || 0
        : first.type === DataSyncInboxItemType.MassChildDeletion
          ? payload.childrenTotal || payload.children?.length || 0
          : (payload.valueCount ?? payload.usageCount ?? 0),
  };
};

/** A button's words, with the target it names. */
const choiceLabel = (t: TFunction, item: DataSyncInboxItemView, choice: InboxChoice) => {
  const payload = item.payload;
  const candidate = payload.candidates?.find((c) => c.localKey === choice.targetLocalKey);
  const record = payload.records?.find((r) => r.primaryKey === choice.targetRecordKey);

  const values: Record<string, string> = { name: peerName(item) };

  if (candidate) values.candidate = candidate.name;
  if (record) values.record = record.name;
  if (choice.action === DataSyncInboxAction.Convert)
    values.type = t(`PropertyType.${payload.remoteSubtype ?? ""}`, {
      defaultValue: payload.remoteSubtype ?? "",
    });

  return t(`dataSync.inbox.action.${actionKey(item.type, choice.action)}`, values);
};

export default function InboxCard({
  card,
  busy,
  error,
  applying = false,
  closedItem,
  backup,
  onResolve,
  onPauseLink,
  onDismissError,
  now,
}: InboxCardProps) {
  const { t } = useTranslation();
  const first = card.items[0];
  const values = cardValues(card);
  // Conflicts of a definition are decided together, a choice per field and device.
  const conflict = isConflict(first) && !!card.localKey;
  const [choices, setChoices] = useState<ConflictChoices>({});
  const [typing, setTyping] = useState<InboxChoice>();
  const [text, setText] = useState("");
  const [newName, setNewName] = useState(
    t<string>("dataSync.plan.separateName", {
      name: first.payload.entityName,
      source: peerName(first),
    }),
  );
  const [backupFirst, setBackupFirst] = useState(true);
  const [preview, setPreview] = useState<DataSyncTypeChangePreview | null>();
  const [previewError, setPreviewError] = useState<Error>();
  const [expanded, setExpanded] = useState(false);
  const destructive = cardIsDestructive(card);
  const disabled = busy || applying || !!closedItem;
  const typeName = DataSyncInboxItemTypeLabel[first.type];

  const send = (choice: InboxChoice, input: { customValue?: string; newName?: string } = {}) => {
    const batch = batchOf([resolveInput(first, choice, input)], backupFirst);
    const needsAsking =
      choice.destructive &&
      (choice.action === DataSyncInboxAction.DeleteHere ||
        choice.action === DataSyncInboxAction.Convert);

    onResolve(
      batch,
      needsAsking
        ? {
            title: choiceLabel(t, first, choice),
            description: t(
              choice.action === DataSyncInboxAction.Convert
                ? "dataSync.inbox.confirm.convert"
                : first.type === DataSyncInboxItemType.ChildDeletedInUse
                  ? "dataSync.inbox.confirm.deleteChild"
                  : "dataSync.inbox.confirm.delete",
              values,
            ),
            warning: backupFirst
              ? t("dataSync.inbox.confirm.backedUp")
              : t("dataSync.inbox.confirm.notBackedUp"),
          }
        : undefined,
    );
  };

  const pick = (choice: InboxChoice) => {
    if (choice.input) {
      setTyping(choice);
      setText("");

      return;
    }
    send(choice);
  };

  const loadPreview = async () => {
    setExpanded((current) => !current);
    if (preview !== undefined || expanded) return;
    setPreviewError(undefined);
    try {
      setPreview((await dataSyncApi.previewInboxItem(first.id)) ?? null);
    } catch (cause) {
      setPreviewError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  };

  return (
    <article
      aria-labelledby={`data-sync-inbox-${card.key}`}
      className={`space-y-3 rounded-xl border p-3 ${
        closedItem ? "border-success/40 bg-success/5" : "border-default-200 bg-content1"
      }`}
      data-card={card.key}
      data-closed={closedItem ? "true" : undefined}
      data-testid="data-sync-inbox-card"
      data-type={typeName}
    >
      <header className="space-y-1">
        <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
          {card.kind && (
            <span className="rounded bg-default-100 px-1.5 py-0.5">
              {t(`dataSync.kind.${card.kind}`, { defaultValue: card.kind })}
            </span>
          )}
          {card.peers.map((peer) => (
            <span
              key={peer.nodeId}
              className="rounded bg-secondary/10 px-1.5 py-0.5 text-secondary"
            >
              {peer.name}
            </span>
          ))}
          <span title={localDateTime(first.updatedAt)}>{timeAgo(t, first.updatedAt, now)}</span>
        </div>
        <h3
          className="break-words text-sm font-semibold [overflow-wrap:anywhere]"
          id={`data-sync-inbox-${card.key}`}
        >
          {t(`dataSync.inbox.type.${headlineKey(card)}`, values)}
        </h3>
      </header>

      {closedItem && (
        <p
          className="text-sm text-success-700 dark:text-success"
          data-testid="data-sync-inbox-closed"
        >
          {t(`dataSync.inbox.closure.${closureKey(closedItem)}`, {
            name: closedItem.closedByName ?? "",
          })}
        </p>
      )}

      {conflict ? (
        <ConflictBody
          card={card}
          choices={choices}
          disabled={disabled}
          onChoose={(path, choice) => setChoices((current) => ({ ...current, [path]: choice }))}
        />
      ) : (
        <ItemBody
          expanded={expanded}
          item={first}
          preview={preview}
          previewError={previewError}
          onTogglePreview={() => void loadPreview()}
        />
      )}

      {error && <DataSyncErrorNotice error={error} onDismiss={onDismissError} />}
      {applying && (
        <p className="text-xs text-primary" data-testid="data-sync-inbox-applying" role="status">
          {t("dataSync.inbox.card.applying")}
        </p>
      )}

      {!closedItem && (
        <div className="space-y-2">
          {typing ? (
            <div className="flex flex-wrap items-center gap-2" data-testid="data-sync-inbox-input">
              <input
                aria-label={t(
                  typing.input === "newName"
                    ? "dataSync.inbox.card.newName"
                    : "dataSync.inbox.card.customValue",
                )}
                className={`${fieldClass} w-60 py-1 text-xs`}
                maxLength={256}
                value={typing.input === "newName" ? newName : text}
                onChange={(event) =>
                  typing.input === "newName"
                    ? setNewName(event.target.value)
                    : setText(event.target.value)
                }
              />
              <button
                className={smallButtonClass}
                data-testid="data-sync-inbox-input-apply"
                disabled={disabled || !(typing.input === "newName" ? newName : text).trim().length}
                type="button"
                onClick={() =>
                  send(
                    typing,
                    typing.input === "newName"
                      ? { newName: newName.trim() }
                      : { customValue: text.trim() },
                  )
                }
              >
                {t("dataSync.inbox.card.apply")}
              </button>
              <button
                className={linkButtonClass}
                type="button"
                onClick={() => setTyping(undefined)}
              >
                {t("dataSync.cancel")}
              </button>
            </div>
          ) : conflict ? (
            <div className="flex flex-wrap gap-2">
              <button
                className={smallButtonClass}
                data-testid="data-sync-inbox-apply"
                disabled={disabled || !conflictDecided(card, choices)}
                type="button"
                onClick={() => onResolve(conflictBatch(card, choices, backupFirst))}
              >
                {t("dataSync.inbox.card.apply")}
              </button>
              {card.items.every((item) =>
                item.allowedActions.includes(DataSyncInboxAction.Detach),
              ) && (
                <button
                  className={smallButtonClass}
                  data-action="Detach"
                  data-testid="data-sync-inbox-action"
                  disabled={disabled}
                  type="button"
                  onClick={() => onResolve(conflictBatch(card, "detach", backupFirst))}
                >
                  {t("dataSync.inbox.action.Detach")}
                </button>
              )}
            </div>
          ) : (
            <div className="flex flex-wrap gap-2">
              {inboxChoices(first).map((choice) => (
                <button
                  key={`${choice.action}-${choice.targetLocalKey ?? choice.targetRecordKey ?? ""}`}
                  className={`${smallButtonClass} ${
                    choice.destructive ? "border-danger/40 text-danger" : ""
                  }`}
                  data-action={actionKey(first.type, choice.action)}
                  data-testid="data-sync-inbox-action"
                  disabled={disabled}
                  type="button"
                  onClick={() => pick(choice)}
                >
                  {choiceLabel(t, first, choice)}
                </button>
              ))}
              {first.type === DataSyncInboxItemType.LargeChange &&
                first.linkId != null &&
                onPauseLink && (
                  <button
                    className={smallButtonClass}
                    data-action="Pause"
                    data-testid="data-sync-inbox-action"
                    disabled={disabled}
                    type="button"
                    onClick={() => onPauseLink(first.linkId!)}
                  >
                    {t("dataSync.inbox.action.PauseLink", { name: peerName(first) })}
                  </button>
                )}
            </div>
          )}
          {destructive && (
            <BackupCheckbox
              backup={backup}
              checked={backupFirst}
              disabled={disabled}
              onChange={setBackupFirst}
            />
          )}
        </div>
      )}
    </article>
  );
}

/** "Back up the database first (about {size}, to {folder})", ticked by default. */
export function BackupCheckbox({
  backup,
  checked,
  disabled,
  onChange,
}: {
  backup: BackupTarget;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}) {
  const { t } = useTranslation();
  const label =
    backup.size && backup.folder
      ? t("dataSync.inbox.backup", { size: backup.size, folder: backup.folder })
      : backup.size
        ? t("dataSync.inbox.backupNoFolder", { size: backup.size })
        : t("dataSync.inbox.backupPlain");

  return (
    <div className="space-y-0.5">
      <label className="flex items-start gap-2 text-xs">
        <input
          checked={checked}
          className="mt-0.5"
          data-testid="data-sync-backup"
          disabled={disabled}
          type="checkbox"
          onChange={(event) => onChange(event.target.checked)}
        />
        <span className="break-all">{label}</span>
      </label>
      <p className="pl-5 text-[11px] text-default-500">{t("dataSync.inbox.backupRestoreHint")}</p>
    </div>
  );
}

/** Last agreed (faint), this device, and each other device, for one field. */
function Comparison({
  outcome,
  others,
}: {
  outcome: DataSyncFieldOutcome;
  /** The other devices' values of the field, when a card holds several. */
  others: { name: string; value?: DataSyncFieldOutcome["remote"] }[];
}) {
  const { t } = useTranslation();

  return (
    <dl
      className="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-3 gap-y-1 text-xs"
      data-testid="data-sync-inbox-comparison"
    >
      {outcome.base && (
        <>
          <dt className="text-default-400">{t("dataSync.inbox.card.lastAgreed")}</dt>
          <dd>
            <DisplayValue faint value={outcome.base} />
          </dd>
        </>
      )}
      <dt className="font-medium">{t("dataSync.thisDevice")}</dt>
      <dd>
        <DisplayValue value={outcome.local} />
      </dd>
      {others.map((other) => (
        <Other key={other.name} name={other.name} value={other.value} />
      ))}
    </dl>
  );
}

function Other({ name, value }: { name: string; value?: DataSyncFieldOutcome["remote"] }) {
  return (
    <>
      <dt className="font-medium text-secondary">{name}</dt>
      <dd>
        <DisplayValue value={value} />
      </dd>
    </>
  );
}

/** Every conflict of one definition: a choice per field and device. */
function ConflictBody({
  card,
  choices,
  disabled,
  onChoose,
}: {
  card: InboxCardModel;
  choices: ConflictChoices;
  disabled: boolean;
  onChoose: (path: string, choice: ConflictChoices[string]) => void;
}) {
  const { t } = useTranslation();

  return (
    <div className="space-y-3">
      {conflictFields(card).map((field) => {
        const first = field.items[0];
        const outcome =
          first.payload.fields.find((candidate) => candidate.path === field.path) ??
          first.payload.fields[0];
        const choice = choices[field.path];
        const group = `data-sync-field-${card.key}-${field.path}`;

        return (
          <fieldset
            key={field.path}
            className="space-y-2 rounded-lg bg-default-50 p-2"
            data-field={field.path}
            data-testid="data-sync-inbox-field"
          >
            <legend className="text-xs font-medium">
              {first.type === DataSyncInboxItemType.ChildRenameConflict
                ? t("dataSync.inbox.card.option", {
                    label: displayText(t, outcome?.base ?? outcome?.local),
                  })
                : pathLabel(t, field.path)}
              {first.payload.usageCount != null && (
                <span className="ml-2 font-normal text-default-500">
                  {t("dataSync.inbox.card.inUse", { count: first.payload.usageCount })}
                </span>
              )}
            </legend>
            {outcome && (
              <Comparison
                others={field.items.map((item) => ({
                  name: peerName(item),
                  value:
                    item.payload.fields.find((candidate) => candidate.path === field.path)
                      ?.remote ?? undefined,
                }))}
                outcome={outcome}
              />
            )}
            <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs" role="radiogroup">
              <label className="inline-flex items-center gap-1.5">
                <input
                  checked={choice?.take === "local"}
                  disabled={disabled}
                  name={group}
                  type="radio"
                  onChange={() => onChoose(field.path, { take: "local" })}
                />
                {t("dataSync.inbox.action.KeepLocal")}
              </label>
              {field.items.map((item) => (
                <label key={item.id} className="inline-flex items-center gap-1.5">
                  <input
                    checked={choice?.take === "remote" && choice.itemId === item.id}
                    disabled={disabled}
                    name={group}
                    type="radio"
                    onChange={() => onChoose(field.path, { take: "remote", itemId: item.id })}
                  />
                  {t("dataSync.inbox.action.UseRemote", { name: peerName(item) })}
                </label>
              ))}
              {field.custom && (
                <label className="inline-flex items-center gap-1.5">
                  <input
                    checked={choice?.take === "custom"}
                    disabled={disabled}
                    name={group}
                    type="radio"
                    onChange={() =>
                      onChoose(field.path, {
                        take: "custom",
                        value: choice?.take === "custom" ? choice.value : "",
                      })
                    }
                  />
                  {t("dataSync.inbox.action.UseCustom")}
                </label>
              )}
            </div>
            {choice?.take === "custom" && (
              <input
                aria-label={t("dataSync.inbox.card.customValue")}
                className={`${fieldClass} w-60 py-1 text-xs`}
                data-testid="data-sync-inbox-custom"
                disabled={disabled}
                maxLength={256}
                value={choice.value}
                onChange={(event) =>
                  onChoose(field.path, { take: "custom", value: event.target.value })
                }
              />
            )}
          </fieldset>
        );
      })}
    </div>
  );
}

/** What one item that is not a conflict has to say, type by type. */
function ItemBody({
  item,
  expanded,
  preview,
  previewError,
  onTogglePreview,
}: {
  item: DataSyncInboxItemView;
  expanded: boolean;
  preview?: DataSyncTypeChangePreview | null;
  previewError?: Error;
  onTogglePreview: () => void;
}) {
  const { t } = useTranslation();
  const payload = item.payload;
  const name = peerName(item);
  const entity = payload.entityName;

  switch (item.type) {
    case DataSyncInboxItemType.TypeChange:
      return (
        <div className="space-y-2 text-xs">
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1">
            <dt className="font-medium">{t("dataSync.thisDevice")}</dt>
            <dd>
              {t(`PropertyType.${payload.localSubtype ?? payload.subtype ?? ""}`, {
                defaultValue: payload.localSubtype ?? payload.subtype ?? "",
              })}
            </dd>
            <dt className="font-medium text-secondary">{name}</dt>
            <dd>
              {t(`PropertyType.${payload.remoteSubtype ?? ""}`, {
                defaultValue: payload.remoteSubtype ?? "",
              })}
            </dd>
          </dl>
          <p className="text-default-500">{t("dataSync.inbox.card.frozen", { entity })}</p>
          <button
            aria-expanded={expanded}
            className={linkButtonClass}
            data-testid="data-sync-inbox-preview-toggle"
            type="button"
            onClick={onTogglePreview}
          >
            {t(expanded ? "dataSync.inbox.card.hidePreview" : "dataSync.inbox.card.showPreview")}
          </button>
          {expanded && (
            <div data-testid="data-sync-inbox-preview">
              <DataSyncErrorNotice error={previewError} />
              {preview === undefined && !previewError && (
                <p role="status">{t("dataSync.loading")}</p>
              )}
              {preview && (
                <div className="space-y-1">
                  <p>
                    {preview.lossyCount > 0
                      ? t("dataSync.inbox.card.previewLossy", {
                          changed: preview.changedCount,
                          count: preview.valueCount,
                          lossy: preview.lossyCount,
                        })
                      : t("dataSync.inbox.card.previewSafe", { count: preview.valueCount })}
                  </p>
                  {preview.samples.length > 0 && (
                    <ul className="space-y-0.5 text-default-500">
                      {preview.samples.map((sample, index) => (
                        <li key={index}>
                          {sample.from ?? ""} → {sample.to ?? ""}
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}
              {preview === null && <p>{t("dataSync.inbox.card.previewNone")}</p>}
            </div>
          )}
        </div>
      );
    case DataSyncInboxItemType.DeletedThere:
      return (
        <p className="text-xs">
          {payload.valueCount
            ? t("dataSync.inbox.card.valuesHere", { count: payload.valueCount })
            : t("dataSync.inbox.card.noValuesHere")}
        </p>
      );
    case DataSyncInboxItemType.ChildDeletedInUse:
      return (
        <div className="space-y-1 text-xs">
          <ChildChips item={item} />
          <p>{t("dataSync.inbox.card.usedHere", { count: payload.usageCount ?? 0 })}</p>
        </div>
      );
    case DataSyncInboxItemType.DeletedHereEditedThere:
      return <p className="text-xs text-default-500">{t("dataSync.inbox.card.restoreEmpty")}</p>;
    case DataSyncInboxItemType.LinkSuggestion:
    case DataSyncInboxItemType.IdentityConflict:
      if (payload.records?.length)
        return (
          <div className="space-y-1 text-xs" data-testid="data-sync-inbox-records">
            <ul className="space-y-0.5">
              {payload.records.map((record) => (
                <li key={record.primaryKey}>
                  {t("dataSync.inbox.card.record", { name, record: record.name })}
                </li>
              ))}
            </ul>
            <p className="text-default-500">{t("dataSync.inbox.card.recordHint")}</p>
          </div>
        );

      return (
        <ul className="space-y-0.5 text-xs" data-testid="data-sync-inbox-candidates">
          {(payload.candidates ?? []).map((candidate) => (
            <li key={candidate.localKey} data-updatable={candidate.updatable}>
              <span className="font-medium">{candidate.name}</span>{" "}
              <span className="text-default-500">
                ·{" "}
                {candidate.updatable
                  ? t(`dataSync.inbox.match.${DataSyncNaturalMatchLabel[candidate.match]}`)
                  : t("dataSync.inbox.match.otherType")}
              </span>
            </li>
          ))}
        </ul>
      );
    case DataSyncInboxItemType.MassChildDeletion:
      return (
        <div className="space-y-1 text-xs">
          <ChildChips item={item} />
          <p className="text-default-500">
            {t("dataSync.inbox.card.frozenMass", { name, entity })}
          </p>
        </div>
      );
    case DataSyncInboxItemType.SuspectedLostUpdate:
      return (
        <div className="space-y-2 text-xs">
          {payload.fields.map((field) => (
            <div key={field.path} className="space-y-1">
              <p className="font-medium">
                {field.path.includes(":")
                  ? t("dataSync.inbox.card.option", { label: displayText(t, field.base) })
                  : pathLabel(t, field.path)}
              </p>
              <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1">
                <dt className="text-default-400">{t("dataSync.inbox.card.synced")}</dt>
                <dd>
                  <DisplayValue value={field.base} />
                </dd>
                <dt className="font-medium">{t("dataSync.inbox.card.now")}</dt>
                <dd>
                  <DisplayValue value={field.local} />
                </dd>
              </dl>
            </div>
          ))}
          <p className="text-default-500">{t("dataSync.inbox.card.lostUpdateHint")}</p>
        </div>
      );
    case DataSyncInboxItemType.LargeChange:
      return (
        <div className="space-y-1 text-xs">
          <ul className="max-h-48 space-y-0.5 overflow-y-auto" data-testid="data-sync-inbox-large">
            {(payload.largeChange ?? []).map((entry, index) => (
              <li key={`${entry.kind}-${entry.name}-${index}`}>
                <span className="font-medium">{entry.name}</span>{" "}
                <span className="text-default-500">
                  ·{" "}
                  {entry.create
                    ? t("dataSync.inbox.card.newDefinition")
                    : t("dataSync.inbox.card.changes", { count: entry.changes })}
                </span>
              </li>
            ))}
          </ul>
          <p className="text-default-500">{t("dataSync.inbox.card.largeHint")}</p>
        </div>
      );
    default:
      return null;
  }
}

/** The options a deletion is about: the first ones, and how many there are in all. */
function ChildChips({ item }: { item: DataSyncInboxItemView }) {
  const { t } = useTranslation();
  const children = item.payload.children ?? [];

  if (!children.length) return null;

  return (
    <div className="space-y-1">
      <div className="flex flex-wrap gap-1" data-testid="data-sync-inbox-children">
        {children.map((child, index) => (
          <DisplayValue key={index} value={child} />
        ))}
      </div>
      {item.payload.childrenTotal > children.length && (
        <p className="text-default-500">
          {t("dataSync.inbox.card.firstOf", {
            shown: children.length,
            count: item.payload.childrenTotal,
          })}
        </p>
      )}
    </div>
  );
}
