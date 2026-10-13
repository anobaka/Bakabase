import type { MappingTreeNode, PathMapping } from "../types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderOutlined } from "@ant-design/icons";

import { federationPeerApi } from "../peerApi";
import { FederationError } from "../transport";

import { buttonClass, ErrorNotice } from "./common";

type Page = { nodes: MappingTreeNode[]; nextCursor?: string; loading?: boolean; error?: Error };
const pageKey = (parent?: string) => JSON.stringify(parent ?? null);

export const mappedDirectoryPreview = (
  chain: MappingTreeNode[],
  mappings: PathMapping[],
): string | undefined => {
  for (let index = chain.length - 1; index >= 0; index--) {
    const mapping = mappings.find((item) => item.sourceRootId === chain[index]!.sourceRootId);

    if (!mapping?.localPath) continue;
    const suffix = chain.slice(index + 1).map((item) => item.name);
    const separator =
      /^[a-z]:/i.test(mapping.localPath) || mapping.localPath.includes("\\") ? "\\" : "/";

    return suffix.length
      ? `${mapping.localPath.replace(/[/\\]+$/, "")}${separator}${suffix.join(separator)}`
      : mapping.localPath;
  }

  return undefined;
};

/** Every request addresses one level only; stale requests can never update another peer's tree. */
export default function MappingTree({
  nodeId,
  active,
  mappings,
  busy,
  onSelect,
  onNodes,
}: {
  nodeId: string;
  active: boolean;
  mappings: PathMapping[];
  busy: boolean;
  onSelect: (node: MappingTreeNode, label: string) => void;
  onNodes: (nodes: { sourceRootId: string; name: string }[]) => void;
}) {
  const { t } = useTranslation();
  const [pages, setPages] = useState<Record<string, Page>>({});
  const [expanded, setExpanded] = useState(new Set<string>());
  const [legacy, setLegacy] = useState(false);
  const legacyRoots = useRef<MappingTreeNode[]>();
  const requests = useRef(new Map<string, AbortController>());
  const generation = useRef(0);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
      generation.current++;
      requests.current.forEach((request) => request.abort());
    };
  }, []);

  const load = async (parent?: string, cursor?: string, chain: MappingTreeNode[] = []) => {
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
      let result;

      if (!parent && legacyRoots.current) {
        const offset = cursor ? Number(cursor) : 0;

        result = {
          nodes: legacyRoots.current.slice(offset, offset + 200),
          nextCursor: offset + 200 < legacyRoots.current.length ? String(offset + 200) : undefined,
        };
      } else {
        try {
          result = await federationPeerApi.mappingTree(nodeId, { parent, cursor }, request.signal);
        } catch (error) {
          if (
            parent ||
            cursor ||
            !(error instanceof FederationError) ||
            !(
              (error.status === 404 && ["EndpointNotFound", "Http404"].includes(error.code)) ||
              (error.status === 501 && error.code === "MappingTreeUnavailable")
            )
          )
            throw error;
          const roots = await federationPeerApi.mappingRoots(nodeId, request.signal);

          if (!valid()) return;
          legacyRoots.current = roots.map((root) => ({
            ...root,
            browseToken: "",
            hasChildren: false,
            resourceCount: 0,
          }));
          setLegacy(true);
          result = {
            nodes: legacyRoots.current.slice(0, 200),
            nextCursor: legacyRoots.current.length > 200 ? "200" : undefined,
          };
        }
      }
      if (!valid()) return;
      const nodes = result.nodes.slice(0, 200);

      setPages((previous) => {
        if (!valid()) return previous;
        const combined = cursor ? [...(previous[key]?.nodes ?? []), ...nodes] : nodes;

        return {
          ...previous,
          [key]: {
            nodes: [...new Map(combined.map((node) => [node.sourceRootId, node])).values()],
            nextCursor: result.nextCursor,
          },
        };
      });
      onNodes(
        nodes.map((node) => ({
          sourceRootId: node.sourceRootId,
          name: [...chain, node].map((entry) => entry.name).join(" / "),
        })),
      );
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
    legacyRoots.current = undefined;
    setLegacy(false);
    setExpanded(new Set());
    setPages({});
    void load();
  };

  const renderPage = (parent?: string, chain: MappingTreeNode[] = []) => {
    const page = pages[pageKey(parent)];

    if (!page) return null;

    return (
      <ul
        aria-label={parent ? undefined : t("federation.mappings.tree.title")}
        className="space-y-1"
        role={parent ? "group" : "tree"}
      >
        {page.nodes.map((node) => {
          const branch = [...chain, node];
          const label = branch.map((item) => item.name).join(" / ");
          const open = expanded.has(node.browseToken);
          const preview = mappedDirectoryPreview(branch, mappings);

          return (
            <li
              key={node.sourceRootId}
              aria-expanded={node.hasChildren ? open : undefined}
              aria-selected={mappings.some((mapping) => mapping.sourceRootId === node.sourceRootId)}
              role="treeitem"
            >
              <div className="flex min-w-0 items-center gap-1 rounded-lg py-1 hover:bg-default-100">
                <button
                  aria-label={t(
                    open ? "federation.mappings.tree.collapse" : "federation.mappings.tree.expand",
                    { name: label },
                  )}
                  className="w-6 shrink-0 text-sm disabled:opacity-30"
                  disabled={!node.hasChildren}
                  type="button"
                  onClick={() => {
                    setExpanded((previous) => {
                      const next = new Set(previous);

                      if (open) next.delete(node.browseToken);
                      else next.add(node.browseToken);

                      return next;
                    });
                    if (!open && !pages[pageKey(node.browseToken)])
                      void load(node.browseToken, undefined, branch);
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
                {!legacy && (
                  <span className="shrink-0 text-xs text-default-400">
                    {node.resourceCount.toLocaleString()}
                  </span>
                )}
                <button
                  aria-label={t("federation.mappings.tree.select", { name: label })}
                  className={`${buttonClass} shrink-0 !px-2 !py-1 !text-xs`}
                  disabled={busy}
                  type="button"
                  onClick={() => onSelect(node, label)}
                >
                  {t("federation.mappings.tree.use")}
                </button>
              </div>
              {node.hasChildren && open && (
                <div className="ml-3 border-l border-default-200 pl-2">
                  {renderPage(node.browseToken, branch)}
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
            <ErrorNotice
              error={page.error}
              onRetry={() => void load(parent, page.nextCursor, chain)}
            />
          </li>
        )}
        {!page.loading && !page.error && page.nodes.length === 0 && (
          <li className="py-2 text-xs text-default-500">{t("federation.mappings.tree.empty")}</li>
        )}
        {page.nextCursor && !page.loading && (
          <li>
            <button
              className={`${buttonClass} !py-1 !text-xs`}
              type="button"
              onClick={() => void load(parent, page.nextCursor, chain)}
            >
              {t("federation.mappings.tree.more")}
            </button>
          </li>
        )}
      </ul>
    );
  };

  return (
    <section className="mt-3 rounded-lg border border-default-200 p-2">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h4 className="text-xs font-medium">{t("federation.mappings.tree.title")}</h4>
        <button className="text-xs text-primary" type="button" onClick={refresh}>
          {t("federation.mappings.tree.refresh")}
        </button>
      </div>
      <p className="mb-2 text-xs text-default-500">
        {t(legacy ? "federation.mappings.tree.legacy" : "federation.mappings.tree.tip")}
      </p>
      <div className="max-h-80 overflow-y-auto pr-1">{renderPage()}</div>
    </section>
  );
}
