import type { ManagementSource } from "./api";
import type BApi from "@/sdk/BApi";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { sourceKey } from "./api";
import { createResourceApi, subscribeResourceOwnerChanges } from "./resourceApi";
import { canReadSourceMetadata, sourceSearchRevision } from "./metadata";
import { useManagementSources } from "./useManagementSources";
import DeviceDot from "./DeviceDot";

export type OwnedCatalogItem<T> = T & { owner?: ManagementSource };
export const catalogItemKey = (item: { id?: number; owner?: ManagementSource }) =>
  JSON.stringify([item.owner?.serverId ?? "local", item.owner?.libraryEpoch ?? "", item.id]);
export const canEditCatalogItem = (item: { owner?: ManagementSource }) =>
  !item.owner || (item.owner.available && !item.owner.metadata?.denied);
export const catalogItemApi = (item: { owner?: ManagementSource }) => createResourceApi(item.owner);
export function catalogOwnerSearch(owner?: ManagementSource) {
  return owner
    ? {
        owner: { serverId: owner.serverId, libraryEpoch: owner.libraryEpoch },
        deviceIds: [owner.serverId],
      }
    : {};
}

/** Native list, native item models. Ownership is metadata, never a second UI or a local ID. */
export function useOwnedCatalog<T extends { id?: number }>(
  localItems: T[],
  read: (api: typeof BApi) => Promise<{ data?: T[]; code?: number; message?: string }>,
) {
  const management = useManagementSources();
  const readRef = useRef(read);

  readRef.current = read;
  const sourcesRef = useRef(management.sources);

  sourcesRef.current = management.sources;
  const revision = JSON.stringify(
    management.sources
      .filter((s) => !s.isLocal)
      .map((s) => [...sourceSearchRevision(s), s.metadata?.data.checkpoint?.catalogRevision]),
  );
  const [remote, setRemote] = useState<Map<string, T[]>>(new Map());
  const [errors, setErrors] = useState<Map<string, string>>(new Map());
  const [loading, setLoading] = useState(false);
  const [reload, setReload] = useState(0);
  const refresh = useCallback(() => {
    management.refresh();
    setReload((n) => n + 1);
  }, [management.refresh]);

  useEffect(() => {
    let cancelled = false;
    const sources = sourcesRef.current.filter((s) => !s.isLocal && canReadSourceMetadata(s));
    const off = sources.map((s) => subscribeResourceOwnerChanges(s, () => setReload((n) => n + 1)));
    const run = async () => {
      setLoading(sources.length > 0);
      const next = new Map<string, T[]>(),
        failures = new Map<string, string>();

      // Catalogs are bounded by the metadata protocol; bound device fan-out as well.
      for (let i = 0; i < sources.length; i += 4) {
        await Promise.all(
          sources.slice(i, i + 4).map(async (source) => {
            try {
              const response = await readRef.current(createResourceApi(source));

              if (response.code) throw new Error(response.message || String(response.code));
              next.set(sourceKey(source), response.data ?? []);
            } catch (error) {
              failures.set(sourceKey(source), String(error));
            }
          }),
        );
        if (cancelled) return;
      }
      if (!cancelled) {
        setRemote(next);
        setErrors(failures);
        setLoading(false);
      }
    };

    void run();

    return () => {
      cancelled = true;
      off.forEach((stop) => stop());
    };
  }, [revision, reload]);
  const items = useMemo(() => {
    const local = management.sources.find((s) => s.isLocal);
    const result: OwnedCatalogItem<T>[] = localItems.map((item) => ({ ...item, owner: local }));

    for (const source of management.sources) {
      if (source.isLocal || !canReadSourceMetadata(source)) continue;
      for (const item of remote.get(sourceKey(source)) ?? [])
        result.push({ ...item, owner: source });
    }

    return result;
  }, [localItems, remote, management.sources]);

  return { items, sources: management.sources, errors, loading, refresh, error: management.error };
}

export function CatalogOwnerMark({ owner }: { owner?: ManagementSource }) {
  const { t } = useTranslation();

  if (!owner || owner.isLocal) return null;

  return (
    <span className="inline-flex shrink-0 items-center gap-1.5">
      <DeviceDot isSource name={owner.name} serverId={owner.serverId} />
      {!owner.available && (
        <span className="text-xs text-warning">{t("management.catalog.readOnly")}</span>
      )}
    </span>
  );
}

export function CatalogSourceStatus({
  sources,
  errors,
  error,
  loading,
}: {
  sources: ManagementSource[];
  errors: Map<string, string>;
  error?: Error;
  loading: boolean;
}) {
  const { t } = useTranslation();
  const unavailable = sources.filter(
    (s) =>
      !s.isLocal && !s.metadata?.denied && (!canReadSourceMetadata(s) || errors.has(sourceKey(s))),
  );

  if (!unavailable.length && !error && !loading) return null;

  return (
    <div className="text-xs text-default-500" role="status">
      {loading && <p>{t("management.catalog.loading")}</p>}
      {error && <p className="text-warning">{t("management.catalog.sourcesFailed")}</p>}
      {unavailable.map((s) => (
        <p key={sourceKey(s)} className="flex items-center gap-1.5 text-warning">
          <DeviceDot name={s.name} serverId={s.serverId} />
          <span className="min-w-0 break-words">
            {t("management.catalog.unavailable", { device: s.name })}
          </span>
        </p>
      ))}
    </div>
  );
}
