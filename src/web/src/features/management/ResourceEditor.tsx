import type { Resource } from "@/core/models/Resource";
import type { IProperty } from "@/components/Property/models";
import type { Library } from "./Definitions";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { ownerClient, type ManagementSource } from "./api";

import { Button, Modal, Spinner } from "@/components/bakaui";
import { useResourceOptionsStore } from "@/stores/options";
import { buildEffectiveScopePriority, selectScopedValue } from "@/core/models/Resource";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import { getDbValueType, getBizValueType } from "@/components/Property/PropertySystem";
import {
  PropertyPool,
  PropertyType,
  propertyValueScopes,
  PropertyValueScope,
} from "@/sdk/constants";

/** Never mounts source-dependent legacy detail actions against the manager's global SDK. */
export function ResourceEditor({
  source,
  ids,
  onClose,
  onUpdated,
}: {
  source: ManagementSource;
  ids: number[];
  onClose: () => void;
  onUpdated: () => void;
}) {
  const { t } = useTranslation();
  const configuredLocalPriority = useResourceOptionsStore(
    (state) => state.data?.propertyValueScopePriority,
  );
  const localPriority = useMemo(() => {
    const priority = configuredLocalPriority?.length
      ? [...configuredLocalPriority]
      : propertyValueScopes.map((scope) => scope.value);

    for (const { value } of propertyValueScopes) {
      if (!priority.includes(value)) {
        if (value === PropertyValueScope.Manual) priority.unshift(value);
        else priority.push(value);
      }
    }

    return priority;
  }, [configuredLocalPriority]);
  const request = useMemo(() => ownerClient(source), [source]);
  const [resources, setResources] = useState<Resource[]>([]);
  const [properties, setProperties] = useState<IProperty[]>([]);
  const [libraries, setLibraries] = useState<Library[]>([]);
  const [chosenLibraries, setChosenLibraries] = useState<number[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [feedback, setFeedback] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [tab, setTab] = useState<"properties" | "libraries">("properties");
  const [propertyFilter, setPropertyFilter] = useState("");
  const load = useCallback(async () => {
    setLoading(true);
    setError(undefined);
    try {
      const [r, p, l] = await Promise.all([
        request<Resource[]>("resourceGet", { ids }),
        request<IProperty[]>("properties"),
        request<Library[]>("libraries"),
      ]);

      if (r.data?.length !== ids.length) throw new Error(t("management.resourcesChanged"));
      setResources(r.data);
      setProperties((p.data ?? []).filter((p) => p.pool !== PropertyPool.Internal));
      setLibraries(l.data ?? []);
      setChosenLibraries(
        (l.data ?? [])
          .filter((library) =>
            r.data!.every((resource) =>
              resource.mediaLibraries?.some((item) => item.id === library.id),
            ),
          )
          .map((library) => library.id),
      );
    } catch (reason) {
      setError(String(reason));
    } finally {
      setLoading(false);
    }
  }, [request, ids]);

  useEffect(() => {
    void load();
  }, [load]);
  const changeValue = async (property: IProperty, value?: string) => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    setFeedback(undefined);
    let succeeded = 0;
    const failures: string[] = [];

    // Bounded sequential owner writes report partial failure; no cross-device transaction is implied.
    for (const id of ids) {
      try {
        await request("resourcePropertyPut", {
          id,
          body: {
            propertyId: property.id,
            isCustomProperty: property.pool === PropertyPool.Custom,
            value,
          },
        });
        succeeded++;
      } catch (reason) {
        failures.push(`#${id}: ${String(reason)}`);
      }
    }
    setFeedback(t("management.writeResult", { count: succeeded, total: ids.length }));
    if (succeeded) {
      await load();
      onUpdated();
    }
    if (failures.length) setError(failures.join("\n"));
    setBusy(false);
  };
  const resource = resources[0];
  const bulk = ids.length > 1;

  return (
    <Modal
      visible
      footer={{ actions: ["cancel"], cancelProps: { children: t("management.close") } }}
      scrollBehavior="inside"
      size="4xl"
      title={
        bulk
          ? t("management.editResources", { count: ids.length })
          : (resource?.displayName ?? t("management.details"))
      }
      onClose={onClose}
    >
      <div className="flex flex-col gap-4">
        <div className="rounded-lg bg-default-100 p-3 text-sm">
          <strong>{source.name}</strong>
          <p className="mt-1 text-xs text-default-500">
            {t("management.editOwnerHelp", { device: source.name })}
          </p>
          {bulk && (
            <p className="mt-1 text-xs text-warning">
              {t("management.bulkHelp", { count: ids.length })}
            </p>
          )}
        </div>
        {!bulk && resource?.path && (
          <p className="break-all font-mono text-xs text-default-500">{resource.path}</p>
        )}
        <div aria-label={t("management.details")} className="flex gap-2">
          {(["properties", "libraries"] as const).map((value) => (
            <Button
              key={value}
              color={tab === value ? "primary" : "default"}
              size="sm"
              onPress={() => setTab(value)}
            >
              {t(`management.${value}`)}
            </Button>
          ))}
        </div>
        {error && (
          <p className="whitespace-pre-wrap break-words text-sm text-danger" role="alert">
            {error}
          </p>
        )}
        {feedback && (
          <p className="text-sm text-success" role="status">
            {feedback}
          </p>
        )}
        {loading ? (
          <Spinner />
        ) : !resources.length ? (
          <Button onPress={load}>{t("management.retry")}</Button>
        ) : tab === "properties" ? (
          <>
            <input
              aria-label={t("management.filterProperties")}
              className="rounded-lg border border-divider bg-content2 px-3 py-2 text-sm"
              placeholder={t("management.filterProperties")}
              value={propertyFilter}
              onChange={(e) => setPropertyFilter(e.target.value)}
            />
            <div className="grid min-w-0 gap-x-6 gap-y-4 md:grid-cols-2">
              {properties
                .filter((p) =>
                  p.name.toLocaleLowerCase().includes(propertyFilter.toLocaleLowerCase()),
                )
                .map((property) => {
                  const values = resource.properties?.[property.pool]?.[property.id];
                  const preference = resource.scopePreferences?.find(
                    (p) => p.propertyPool === property.pool && p.propertyId === property.id,
                  );
                  const priority = buildEffectiveScopePriority(
                    // The owner sorts values using its own global/profile configuration.
                    // Never substitute this manager's settings for a foreign resource.
                    source.isLocal
                      ? localPriority
                      : (values?.values?.map((value) => value.scope) ?? []),
                    preference,
                    values?.profileScopePriority,
                  );
                  const value = bulk ? undefined : selectScopedValue(values?.values, priority);
                  const fileValue = property.type === PropertyType.Attachment;

                  return (
                    <div
                      key={`${property.pool}:${property.id}`}
                      className="min-w-0 rounded-lg border border-divider p-3"
                    >
                      <div className="mb-2 text-xs font-medium text-default-500">
                        {property.name}
                      </div>
                      {fileValue ? (
                        <p className="text-xs text-default-500">{t("management.attachmentHelp")}</p>
                      ) : (
                        <PropertyValueRenderer
                          bizValue={serializeStandardValue(
                            value?.aliasAppliedBizValue ?? value?.bizValue,
                            getBizValueType(property.type),
                          )}
                          dbValue={serializeStandardValue(
                            value?.value,
                            getDbValueType(property.type),
                          )}
                          isReadonly={busy}
                          property={property}
                          onValueChange={(next) => changeValue(property, next)}
                        />
                      )}
                    </div>
                  );
                })}
            </div>
          </>
        ) : (
          <div className="flex flex-col gap-4">
            <p className="text-xs text-default-500">
              {t(bulk ? "management.replaceLibrariesHelp" : "management.libraryMembershipHelp")}
            </p>
            <div className="flex flex-wrap gap-2">
              {libraries.map((library) => (
                <label
                  key={library.id}
                  className="flex items-center gap-2 rounded-md border border-divider px-3 py-2 text-sm"
                >
                  <input
                    checked={chosenLibraries.includes(library.id)}
                    disabled={busy}
                    type="checkbox"
                    onChange={(e) =>
                      setChosenLibraries((current) =>
                        e.target.checked
                          ? [...current, library.id]
                          : current.filter((id) => id !== library.id),
                      )
                    }
                  />
                  {library.name}
                </label>
              ))}
            </div>
            <Button
              className="self-start"
              color="primary"
              isLoading={busy}
              onPress={async () => {
                setBusy(true);
                setError(undefined);
                try {
                  await request("resourceLibrariesPut", {
                    body: { ids, mediaLibraryIds: chosenLibraries },
                  });
                  setFeedback(
                    t("management.writeResult", { count: ids.length, total: ids.length }),
                  );
                  onUpdated();
                } catch (reason) {
                  setError(String(reason));
                } finally {
                  setBusy(false);
                }
              }}
            >
              {t("management.saveLibraries")}
            </Button>
          </div>
        )}
      </div>
    </Modal>
  );
}
