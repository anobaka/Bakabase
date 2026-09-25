import type { ReactNode } from "react";
import type { DataSyncEntityStatusView } from "../api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { AiOutlineSync } from "react-icons/ai";

import { dataSyncApi, isRefusedHere } from "../api";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import { useDataSyncWindow } from "../hooks/useDataSyncWindow";
import { DATA_SYNC_ROUTE, dataSyncAddRoute } from "../routes";
import { useDataSyncStore } from "../stores/dataSync";
import { isTooLargeToSync, overallStatus } from "../viewModels";

import DataSyncHelp from "./DataSyncHelp";
import EntitySyncBadge from "./EntitySyncBadge";
import EntitySyncMenu from "./EntitySyncMenu";
import { DataSyncErrorNotice, linkButtonClass, smallButtonClass, toneDot } from "./common";

import ConfirmDialog from "@/features/federation/components/ConfirmDialog";
import { useCanAdministerShownServer } from "@/stores/remoteAccess";

/*
 * What data sync adds to the pages that list definitions — Properties (hook H-props) and
 * Extension groups (H-ext): a header link with the status dot, a badge per row saying how that
 * definition syncs, the row's sync choices, and on an empty Properties page a way to bring the
 * properties over from another device. Shown only to a window that may administer the server
 * it shows (spec §11.3), and never where the server refuses data sync to this window.
 */

/** Whether the definitions pages show data sync here. */
export const useDefinitionPagesSync = () => {
  const administer = useCanAdministerShownServer();
  const reach = useDataSyncWindow();
  const refused = useDataSyncStore((state) => state.reach === "refused");

  return administer && reach !== "notAllowed" && reach !== "asking" && !refused;
};

/** "Data sync", with the dot of how it is going, in the page's header. */
export function DataSyncHeaderLink() {
  const { t } = useTranslation();
  const shown = useDefinitionPagesSync();
  const status = useDataSyncStore((state) => state.status);
  const line = overallStatus(t, status);

  if (!shown) return null;

  return (
    <span className="inline-flex items-center gap-0.5">
      <Link
        className={`${smallButtonClass} relative gap-1.5`}
        data-testid="data-sync-header-link"
        title={line?.text}
        to={DATA_SYNC_ROUTE}
      >
        <AiOutlineSync aria-hidden />
        {t("dataSync.title")}
        {line && (
          <span
            aria-hidden
            className={`h-2 w-2 rounded-full ${toneDot[line.tone]}`}
            data-testid="data-sync-header-dot"
            data-tone={line.tone}
          />
        )}
        {line && <span className="sr-only">{line.text}</span>}
      </Link>
      <DataSyncHelp />
    </span>
  );
}

export interface DefinitionSync {
  shown: boolean;
  /** How each definition syncs, by local key (its id as an invariant string). */
  byKey: Map<string, DataSyncEntityStatusView>;
  actions: ReturnType<typeof useDataSyncActions>;
  /** The confirmation and any failure: rendered once, anywhere on the page. */
  host: ReactNode;
  reload: () => Promise<void>;
}

/**
 * How every definition of one kind syncs, for a page listing them; read again after each
 * choice, and when an apply wrote definitions of that kind — `onApplied` then lets the page read
 * its own list again.
 */
export function useDefinitionSync(kind: string, onApplied?: () => void): DefinitionSync {
  const shown = useDefinitionPagesSync();
  const [byKey, setByKey] = useState<Map<string, DataSyncEntityStatusView>>(new Map());
  const [error, setError] = useState<Error>();
  const [refused, setRefused] = useState(false);
  const lastApplied = useDataSyncStore((state) => state.lastApplied);
  const applied = useRef(onApplied);

  applied.current = onApplied;

  const reload = useCallback(async () => {
    if (!shown) return;
    try {
      const entities = await dataSyncApi.entities(kind);

      setByKey(new Map(entities.map((entity) => [entity.localKey, entity])));
      setError(undefined);
    } catch (cause) {
      if (isRefusedHere(cause)) setRefused(true);
      else setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, [kind, shown]);
  const actions = useDataSyncActions(() => reload());

  useEffect(() => {
    void reload();
  }, [reload]);

  useEffect(() => {
    if (!lastApplied?.kinds.includes(kind)) return;
    void reload();
    applied.current?.();
  }, [lastApplied, kind, reload]);

  const host = shown && !refused && (
    <>
      {error && (
        <div className="text-xs" data-testid="data-sync-definitions-error">
          <DataSyncErrorNotice error={error} onRetry={() => void reload()} />
        </div>
      )}
      {actions.error && (
        <DataSyncErrorNotice error={actions.error} onDismiss={() => actions.setError(undefined)} />
      )}
      {actions.confirmation && (
        <ConfirmDialog
          busy={actions.busy}
          description={actions.confirmation.description}
          error={actions.confirmationError}
          title={actions.confirmation.title}
          warning={actions.confirmation.warning}
          onCancel={actions.cancelConfirmation}
          onConfirm={actions.confirmCurrent}
        />
      )}
    </>
  );

  return {
    shown: shown && !refused,
    byKey,
    actions,
    host: host || null,
    reload,
  };
}

/** One row's sync: its badge, its choices, and the hint for a property too large to sync whole. */
export function DefinitionSyncRow({
  sync,
  kind,
  localKey,
  name,
  offersDefinitionOnly = false,
  menu = true,
}: {
  sync: DefinitionSync;
  kind: string;
  localKey: string;
  name: string;
  offersDefinitionOnly?: boolean;
  /** The row's choices; the Extension groups page shows the badge only. */
  menu?: boolean;
}) {
  const { t } = useTranslation();
  const entity = sync.byKey.get(localKey);

  if (!sync.shown || !entity) return null;

  return (
    <span
      className="inline-flex flex-wrap items-center gap-1.5"
      data-testid="data-sync-definition-row"
    >
      <EntitySyncBadge entity={entity} />
      {isTooLargeToSync(entity) && offersDefinitionOnly && menu && (
        <button
          className={linkButtonClass}
          data-testid="data-sync-too-many-options"
          disabled={sync.actions.busy}
          type="button"
          onClick={() =>
            sync.actions.confirm({
              title: t("dataSync.entity.definitionOnly.onTitle", { name }),
              description: t("dataSync.entity.definitionOnly.everyDevice"),
              action: () => dataSyncApi.setEntitySync(kind, localKey, { childrenLocal: true }),
              refresh: ["dataSync"],
            })
          }
        >
          {t("dataSync.entity.tooManyOptions")}
        </button>
      )}
      {menu && (
        <EntitySyncMenu
          actions={sync.actions}
          entity={entity}
          kind={kind}
          name={name}
          offersDefinitionOnly={offersDefinitionOnly}
        />
      )}
    </span>
  );
}

/** Under an empty Properties page: bring them over from another device. */
export function DataSyncEmptyStateLine() {
  const { t } = useTranslation();
  const shown = useDefinitionPagesSync();

  if (!shown) return null;

  return (
    <p className="text-sm text-default-500" data-testid="data-sync-empty-state-line">
      {t("customProperty.empty.syncHint")}{" "}
      <Link className="text-primary-700 underline" to={dataSyncAddRoute}>
        {t("customProperty.action.syncWithDevice")}
      </Link>
    </p>
  );
}
