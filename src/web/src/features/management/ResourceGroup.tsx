import type { Resource } from "@/core/models/Resource";

import { useTranslation } from "react-i18next";
import { useEffect, useMemo, useRef, useState } from "react";

import { ownerClient, ownedResourceKey, type ManagementSource } from "./api";
import { ResourceEditor } from "./ResourceEditor";
import OwnedResourcePlay from "./OwnedResourcePlay";

import { Button, Spinner } from "@/components/bakaui";

const pageSize = 30;

export function ResourceGroup({ source, keyword }: { source: ManagementSource; keyword: string }) {
  const { t } = useTranslation();
  const request = useMemo(() => ownerClient(source), [source]);
  const [page, setPage] = useState(1);
  const [resources, setResources] = useState<Resource[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const [revision, setRevision] = useState(0);
  const [selected, setSelected] = useState<number[]>([]);
  const [editing, setEditing] = useState<number[]>();
  const searchSequence = useRef(0);
  const previousKeyword = useRef(keyword);

  // Keyword changes reset this source's pagination before its next request.
  if (previousKeyword.current !== keyword) {
    previousKeyword.current = keyword;
    if (page !== 1) setPage(1);
  }
  useEffect(() => {
    const sequence = ++searchSequence.current;

    if (!source.available) return;
    setLoading(true);
    setError(undefined);
    setSelected([]);
    request<Resource[]>("resourceSearch", { body: { keyword, page, pageSize } })
      .then((response) => {
        if (sequence !== searchSequence.current) return;
        setResources(response.data ?? []);
        setTotal(response.totalCount ?? 0);
      })
      .catch((reason) => {
        if (sequence === searchSequence.current) {
          setResources([]);
          setError(String(reason));
        }
      })
      .finally(() => {
        if (sequence === searchSequence.current) setLoading(false);
      });

    return () => {
      searchSequence.current++;
    };
  }, [source, request, keyword, page, revision]);
  const refresh = () => setRevision((value) => value + 1);

  return (
    <section aria-label={source.name} className="rounded-lg border border-divider">
      <header className="flex flex-wrap items-center gap-3 border-b border-divider px-4 py-3">
        <h3 className="min-w-0 flex-1 font-medium">
          {source.name}{" "}
          <span className="text-xs font-normal text-default-500">
            {source.isLocal ? t("management.thisDevice") : t("management.owner")}
          </span>
        </h3>
        {!!selected.length && (
          <Button size="sm" onPress={() => setEditing(selected)}>
            {t("management.editResources", { count: selected.length })}
          </Button>
        )}
        {source.available && !error && !loading && (
          <span className="text-xs text-default-500">
            {t("management.resourceCount", { count: total })}
          </span>
        )}
      </header>
      {!source.available ? (
        <p className="p-4 text-sm text-warning" role="status">
          {t("management.unavailable")} {source.error}
        </p>
      ) : error ? (
        <div className="p-4 text-sm text-danger" role="alert">
          {error}{" "}
          <Button size="sm" onPress={refresh}>
            {t("management.retry")}
          </Button>
        </div>
      ) : loading ? (
        <div className="flex justify-center p-8">
          <Spinner />
        </div>
      ) : !resources.length ? (
        <p className="p-8 text-center text-sm text-default-500">{t("management.empty")}</p>
      ) : (
        <>
          <div className="grid grid-cols-1 gap-px bg-divider sm:grid-cols-2 xl:grid-cols-3">
            {resources.map((resource) => (
              <article
                key={ownedResourceKey(source, resource.id)}
                className="flex min-w-0 items-center gap-3 bg-content1 p-3"
              >
                <input
                  aria-label={t("management.selectResource", {
                    name: resource.displayName ?? resource.path ?? resource.id,
                  })}
                  checked={selected.includes(resource.id)}
                  type="checkbox"
                  onChange={(e) =>
                    setSelected((current) =>
                      e.target.checked
                        ? [...current, resource.id]
                        : current.filter((id) => id !== resource.id),
                    )
                  }
                />
                <button
                  className="min-w-0 flex-1 rounded text-left focus-visible:outline focus-visible:outline-primary"
                  onClick={() => setEditing([resource.id])}
                >
                  <span className="block truncate text-sm font-medium">
                    {resource.displayName || resource.path || `#${resource.id}`}
                  </span>
                  <span className="mt-1 block truncate text-xs text-default-500">
                    {resource.mediaLibraries?.map((library) => library.name).join(" · ") ||
                      t("management.noLibrary")}
                  </span>
                  <span
                    className="mt-1 block truncate font-mono text-[11px] text-default-400"
                    title={resource.path}
                  >
                    {resource.path || t("management.metadataOnly")}
                  </span>
                </button>
                <OwnedResourcePlay resource={resource} source={source} />
              </article>
            ))}
          </div>
          <footer className="flex items-center justify-end gap-3 border-t border-divider p-3 text-xs text-default-500">
            <span>
              {t("management.sourcePage", {
                page,
                total: Math.max(1, Math.ceil(total / pageSize)),
              })}
            </span>
            <Button
              isDisabled={page === 1}
              size="sm"
              variant="light"
              onPress={() => setPage((value) => value - 1)}
            >
              {t("management.previous")}
            </Button>
            <Button
              isDisabled={page * pageSize >= total}
              size="sm"
              variant="light"
              onPress={() => setPage((value) => value + 1)}
            >
              {t("management.next")}
            </Button>
          </footer>
        </>
      )}
      {editing && (
        <ResourceEditor
          key={JSON.stringify(editing)}
          ids={editing}
          source={source}
          onClose={() => setEditing(undefined)}
          onUpdated={refresh}
        />
      )}
    </section>
  );
}
