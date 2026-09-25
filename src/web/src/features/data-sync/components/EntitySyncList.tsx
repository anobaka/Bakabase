import type { DataSyncEntityStatusView } from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { dataSyncKinds } from "../viewModels";

import { DataSyncErrorNotice } from "./common";
import EntitySyncBadge from "./EntitySyncBadge";
import EntitySyncMenu from "./EntitySyncMenu";

import BApi from "@/sdk/BApi";
import { DataSyncEntitySyncState, PropertyType } from "@/sdk/constants";

/*
 * Every definition of one kind with how it syncs, and the choices for each: the page's view of
 * what the Properties and Extension groups pages show row by row. A definition's local key is
 * its id as an invariant string, so names come from those pages' own listings.
 */

/** Property types whose options can be left out of sync ("sync the definition only"). */
const withOptions = new Set<number>([
  PropertyType.SingleChoice,
  PropertyType.MultipleChoice,
  PropertyType.Tags,
  PropertyType.Multilevel,
]);

interface Named {
  name: string;
  offersDefinitionOnly: boolean;
}

const quiet = { showErrorToast: false } as const;

/** Each definition's name, by local key. */
const namesOf = async (kind: string): Promise<Map<string, Named>> => {
  if (kind === "customProperty") {
    const rsp = await BApi.customProperty.getAllCustomProperties(undefined, quiet);

    return new Map(
      (rsp.data ?? []).map((property) => [
        String(property.id),
        { name: property.name, offersDefinitionOnly: withOptions.has(property.type) },
      ]),
    );
  }
  if (kind === "extensionGroup") {
    const rsp = await BApi.extensionGroup.getAllExtensionGroups(quiet);

    return new Map(
      (rsp.data ?? []).map((group) => [
        String(group.id),
        { name: group.name, offersDefinitionOnly: false },
      ]),
    );
  }

  return new Map();
};

/** A definition this device keeps apart from syncing in any way. */
const isApart = (entity: DataSyncEntityStatusView) =>
  entity.state !== DataSyncEntitySyncState.Synced ||
  entity.childrenLocal ||
  entity.heldAtSource != null ||
  entity.localOnlyChildren > 0 ||
  entity.heldChildren > 0;

export default function EntitySyncList({
  actions,
  version,
  initialOnlyApart = false,
}: {
  actions: DataSyncPanelActions;
  /** Changes whenever the page re-reads data sync: the list reads again with it. */
  version: number;
  /** Open on the definitions that are not synced whole, e.g. from a link's "Show". */
  initialOnlyApart?: boolean;
}) {
  const { t } = useTranslation();
  const [kind, setKind] = useState(dataSyncKinds[0]);
  const [onlyApart, setOnlyApart] = useState(initialOnlyApart);
  const [entities, setEntities] = useState<DataSyncEntityStatusView[]>();
  const [names, setNames] = useState<Map<string, Named>>(new Map());
  const [error, setError] = useState<Error>();

  const load = useCallback(async () => {
    setError(undefined);
    try {
      const [list, named] = await Promise.all([
        dataSyncApi.entities(kind),
        namesOf(kind).catch(() => new Map<string, Named>()),
      ]);

      setEntities(list);
      setNames(named);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, [kind]);

  useEffect(() => {
    void load();
  }, [load, version]);

  useEffect(() => setOnlyApart(initialOnlyApart), [initialOnlyApart]);

  const shown = (entities ?? []).filter((entity) => !onlyApart || isApart(entity));
  const nameOf = (entity: DataSyncEntityStatusView) =>
    names.get(entity.localKey)?.name ??
    t<string>("dataSync.entity.unnamed", { key: entity.localKey });

  return (
    <div className="space-y-3" data-testid="data-sync-entities">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div aria-label={t("dataSync.entity.kinds")} className="flex gap-1" role="tablist">
          {dataSyncKinds.map((item) => (
            <button
              key={item}
              aria-selected={kind === item}
              className={`rounded-md px-3 py-1.5 text-sm ${
                kind === item ? "bg-primary/10 font-medium text-primary" : "hover:bg-default-100"
              }`}
              role="tab"
              type="button"
              onClick={() => {
                setKind(item);
                setEntities(undefined);
              }}
            >
              {t(`dataSync.kind.${item}`, { defaultValue: item })}
            </button>
          ))}
        </div>
        <label className="inline-flex items-center gap-2 text-xs">
          <input
            checked={onlyApart}
            type="checkbox"
            onChange={(event) => setOnlyApart(event.target.checked)}
          />
          {t("dataSync.entity.onlyApart")}
        </label>
      </div>
      <DataSyncErrorNotice error={error} onRetry={() => void load()} />
      {!entities && !error && (
        <p className="text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      {entities && shown.length === 0 && (
        <p className="text-sm text-default-500">
          {t(onlyApart ? "dataSync.entity.noneApart" : "dataSync.entity.none")}
        </p>
      )}
      {shown.length > 0 && (
        <ul className="divide-y divide-default-100 rounded-lg border border-default-200">
          {shown.map((entity) => {
            const named = names.get(entity.localKey);

            return (
              <li
                key={entity.localKey}
                className="flex items-center gap-3 px-3 py-2 text-sm"
                data-local-key={entity.localKey}
              >
                <span className="min-w-0 flex-1 truncate">{nameOf(entity)}</span>
                {entity.localOnlyChildren > 0 && (
                  <span className="shrink-0 text-xs text-default-500">
                    {t("dataSync.entity.localOnlyChildren", { count: entity.localOnlyChildren })}
                  </span>
                )}
                <EntitySyncBadge entity={entity} />
                <EntitySyncMenu
                  actions={actions}
                  entity={entity}
                  kind={kind}
                  name={nameOf(entity)}
                  offersDefinitionOnly={named?.offersDefinitionOnly ?? false}
                />
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
