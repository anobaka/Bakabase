import type { ManagedMappingTreeNode, ManagedMappingTreePage } from "./managedMappingTypes";
import type { ManagedServerPathMapping } from "../types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderOutlined } from "@ant-design/icons";

import { managedServerApi } from "../serverApi";
import { mappedDirectoryPreview } from "../pathMappings";
import { FederationError } from "../transport";

import { buttonClass, ErrorNotice } from "./common";

export { mappedDirectoryPreview } from "../pathMappings";

export type ManagedMappingTreeLoader = (
  serverId: string,
  options: { parent?: string; cursor?: string; libraryEpoch?: string },
  signal?: AbortSignal,
) => Promise<ManagedMappingTreePage>;

type Page = {
  nodes: ManagedMappingTreeNode[];
  nextCursor?: string;
  loading?: boolean;
  error?: Error;
};
const pageKey = (parent?: string) => JSON.stringify(parent ?? null);

/** Every request addresses one level only; stale requests can never update another server's tree. */
export default function ManagedMappingTree({
  serverId,
  active,
  mappings,
  busy,
  onSelect,
  loadPage = managedServerApi.mappingTree,
}: {
  serverId: string;
  active: boolean;
  mappings: ManagedServerPathMapping[];
  busy: boolean;
  onSelect: (node: ManagedMappingTreeNode, label: string) => void;
  loadPage?: ManagedMappingTreeLoader;
}) {
  const { t } = useTranslation();
  const [pages, setPages] = useState<Record<string, Page>>({});
  const [expanded, setExpanded] = useState(new Set<string>());
  const requests = useRef(new Map<string, AbortController>());
  const generation = useRef(0);
  const epoch = useRef<string>();
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
      generation.current++;
      requests.current.forEach((request) => request.abort());
    };
  }, []);

  const load = async (parent?: string, cursor?: string) => {
    const key = pageKey(parent);

    requests.current.get(key)?.abort();
    const request = new AbortController();
    const current = generation.current;
    const valid = () =>
      mounted.current &&
      !request.signal.aborted &&
      current === generation.current &&
      requests.current.get(key) === request;

    requests.current.set(key, request);
    setPages((previous) => ({
      ...previous,
      [key]: {
        nodes: previous[key]?.nodes ?? [],
        nextCursor: previous[key]?.nextCursor,
        loading: true,
      },
    }));
    try {
      const result = await loadPage(
        serverId,
        { parent, cursor, libraryEpoch: epoch.current },
        request.signal,
      );

      if (!valid()) return;
      if (epoch.current && result.libraryEpoch !== epoch.current)
        throw new FederationError("LibraryEpochChanged", "", 409);
      epoch.current = result.libraryEpoch;
      const nodes = result.nodes.slice(0, 200);

      setPages((previous) => {
        if (!valid()) return previous;
        const combined = cursor ? [...(previous[key]?.nodes ?? []), ...nodes] : nodes;

        return {
          ...previous,
          [key]: {
            nodes: [...new Map(combined.map((node) => [node.serverPath, node])).values()],
            nextCursor: result.nextCursor,
          },
        };
      });
    } catch (error) {
      if (valid())
        setPages((previous) =>
          valid()
            ? {
                ...previous,
                [key]: {
                  ...previous[key]!,
                  loading: false,
                  error: error instanceof Error ? error : new Error(String(error)),
                },
              }
            : previous,
        );
    }
  };

  useEffect(() => {
    if (active && !pages[pageKey()]) void load();
  }, [active]);
  const refresh = () => {
    generation.current++;
    requests.current.forEach((request) => request.abort());
    requests.current.clear();
    epoch.current = undefined;
    setExpanded(new Set());
    setPages({});
    void load();
  };

  const renderPage = (parent?: string, chain: ManagedMappingTreeNode[] = []) => {
    const page = pages[pageKey(parent)];

    if (!page) return null;

    return (
      <ul
        aria-label={parent ? undefined : t("federation.managedMappings.tree.title")}
        className="space-y-1"
        role={parent ? "group" : "tree"}
      >
        {page.nodes.map((node) => {
          const branch = [...chain, node];
          const label = node.serverPath;
          const open = expanded.has(node.serverPath);
          const preview = mappedDirectoryPreview(node.serverPath, mappings);

          return (
            <li
              key={node.serverPath}
              aria-expanded={node.hasChildren ? open : undefined}
              aria-selected={mappings.some((mapping) => mapping.serverPath === node.serverPath)}
              role="treeitem"
            >
              <div className="flex min-w-0 items-center gap-1 rounded-lg py-1 hover:bg-default-100">
                <button
                  aria-label={t(
                    open
                      ? "federation.managedMappings.tree.collapse"
                      : "federation.managedMappings.tree.expand",
                    { name: label },
                  )}
                  className="w-6 shrink-0 text-sm disabled:opacity-30"
                  disabled={busy || !node.hasChildren}
                  type="button"
                  onClick={() => {
                    setExpanded((previous) => {
                      const next = new Set(previous);

                      if (open) next.delete(node.serverPath);
                      else next.add(node.serverPath);

                      return next;
                    });
                    if (!open && !pages[pageKey(node.serverPath)]) void load(node.serverPath);
                  }}
                >
                  {node.hasChildren ? (open ? "▾" : "▸") : "·"}
                </button>
                <FolderOutlined className="shrink-0 text-default-500" />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm" title={label}>
                    {node.name}
                  </p>
                  {preview && <p className="break-all text-xs text-default-500">→ {preview}</p>}
                </div>
                <span className="shrink-0 text-xs text-default-400">
                  {node.resourceCount.toLocaleString()}
                </span>
                <button
                  aria-label={t("federation.managedMappings.tree.select", { name: label })}
                  className={`${buttonClass} shrink-0 !px-2 !py-1 !text-xs`}
                  disabled={busy}
                  type="button"
                  onClick={() => onSelect(node, label)}
                >
                  {t("federation.managedMappings.tree.use")}
                </button>
              </div>
              {node.hasChildren && open && (
                <div className="ml-3 border-l border-default-200 pl-2">
                  {renderPage(node.serverPath, branch)}
                </div>
              )}
            </li>
          );
        })}
        {page.loading && (
          <li className="py-2 text-xs" role="status">
            {t("federation.loading")}
          </li>
        )}
        {page.error && (
          <li>
            <ErrorNotice error={page.error} onRetry={() => void load(parent, page.nextCursor)} />
          </li>
        )}
        {!page.loading && !page.error && page.nodes.length === 0 && (
          <li className="py-2 text-xs text-default-500">
            {t("federation.managedMappings.tree.empty")}
          </li>
        )}
        {page.nextCursor && !page.loading && (
          <li>
            <button
              className={`${buttonClass} !py-1 !text-xs`}
              type="button"
              onClick={() => void load(parent, page.nextCursor)}
            >
              {t("federation.managedMappings.tree.more")}
            </button>
          </li>
        )}
      </ul>
    );
  };

  return (
    <section className="mt-3 rounded-lg border border-default-200 p-2">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h4 className="text-xs font-medium">{t("federation.managedMappings.tree.title")}</h4>
        <button className="text-xs text-primary" disabled={busy} type="button" onClick={refresh}>
          {t("federation.managedMappings.tree.refresh")}
        </button>
      </div>
      <p className="mb-2 text-xs text-default-500">{t("federation.managedMappings.tree.tip")}</p>
      <div className="max-h-80 overflow-y-auto pr-1">{renderPage()}</div>
    </section>
  );
}
