import type { CommonLibraryQuery, ResourceRef } from "./types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router-dom";
import { AiOutlineDatabase, AiOutlineFile, AiOutlineSearch } from "react-icons/ai";

import {
  buttonClass,
  ErrorNotice,
  FederationAccess,
  fieldClass,
  panelClass,
  primaryClass,
  SourceBadge,
} from "./components/common";
import ResourceDetail from "./components/ResourceDetail";
import { useFederationStatus } from "./hooks/useFederationStatus";
import { useFederatedQuery } from "./hooks/useFederatedQuery";
import { readResourceRef, withResourceRef } from "./navigation";
import { resourceKey } from "./types";
import { FederationError } from "./transport";
import { federationPeerApi } from "./peerApi";

import { resourceSources } from "@/sdk/constants";

type Scope = "local" | "all" | "selected";

export default function LibraryPage() {
  return (
    <FederationAccess>
      <Library />
    </FederationAccess>
  );
}

function Library() {
  const { t } = useTranslation();
  const { status, loading, error, refresh } = useFederationStatus();
  const { state, search, nextPage, cancel, reset } = useFederatedQuery();
  const [params, setParams] = useSearchParams();
  const [preferences] = useState(() => {
    try {
      const value = JSON.parse(localStorage.getItem("federation.scope") || "{}");

      return {
        scope: value.scope,
        sources: Array.isArray(value.sources)
          ? (value.sources.filter((id: unknown) => typeof id === "string") as string[])
          : [],
      };
    } catch {
      return { scope: "local", sources: [] as string[] };
    }
  });
  const savedScope = params.get("scope") ?? preferences.scope;
  const scope: Scope = savedScope === "all" || savedScope === "selected" ? savedScope : "local";
  const selected = params.has("scope") ? params.getAll("source") : preferences.sources;
  const scopeKey = JSON.stringify([scope, selected]);
  const [text, setText] = useState("");
  const [availability, setAvailability] =
    useState<NonNullable<CommonLibraryQuery["fileAvailability"]>>("Any");
  const [sort, setSort] = useState<CommonLibraryQuery["sort"]>("NameAsc");
  const [sourceKinds, setSourceKinds] = useState<number[]>([]);
  const [submittedText, setSubmittedText] = useState("");
  const [now, setNow] = useState(Date.now());
  const [enabling, setEnabling] = useState(false);
  const [enableError, setEnableError] = useState<Error>();
  const initialized = useRef<string>();
  const lastIdentity = useRef<string>();
  const identityKey = status
    ? JSON.stringify([status.identity.nodeId, status.identity.libraryEpoch])
    : undefined;
  const queryKey = JSON.stringify([identityKey, scopeKey]);
  const detailRef = readResourceRef(params);

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);

    return () => clearInterval(timer);
  }, []);

  const nodeIds = !status
    ? []
    : scope === "local"
      ? [status.identity.nodeId]
      : scope === "all"
        ? [
            status.identity.nodeId,
            ...status.peers
              .filter((peer) => peer.enabled && peer.outboundGrant)
              .map((peer) => peer.nodeId),
          ]
        : selected;
  const names = new Map(status?.peers.map((peer) => [peer.nodeId, peer.label]));
  const sources = status
    ? [
        { nodeId: status.identity.nodeId, label: status.identity.name, available: true },
        ...status.peers.map((peer) => ({
          nodeId: peer.nodeId,
          label: peer.label,
          available: peer.enabled && !!peer.outboundGrant,
        })),
      ]
    : [];

  // Saved selections can outlive a forgotten peer or a cloned local identity.
  // Keep them visible and removable instead of silently changing query coverage.
  for (const nodeId of new Set(selected)) {
    if (!sources.some((source) => source.nodeId === nodeId))
      sources.push({ nodeId, label: nodeId, available: false });
  }

  if (status) names.set(status.identity.nodeId, status.identity.name);

  const submit = () => {
    if (status?.browsingEnabled !== true || !nodeIds.length) return;
    setSubmittedText(text.trim());
    void search({
      nodeIds,
      query: {
        queryContractVersion: 1,
        text: text.trim(),
        fileAvailability: availability,
        sort,
        sourceKinds,
      },
      pageSize: 50,
    });
  };

  useEffect(() => {
    if (!status) return;
    const identityChanged =
      lastIdentity.current !== undefined && lastIdentity.current !== identityKey;

    lastIdentity.current = identityKey;
    if (status.browsingEnabled !== true) {
      initialized.current = undefined;
      reset();
      if (detailRef) setParams(withResourceRef(params), { replace: true });

      return;
    }
    if (identityChanged) {
      reset();
      if (detailRef) setParams(withResourceRef(params), { replace: true });
    }
    if (initialized.current === queryKey) return;
    initialized.current = queryKey;
    if (!nodeIds.length) {
      reset();

      return;
    }
    submit();
  }, [status, queryKey]);

  const updateScope = (next: Scope, sources = selected) => {
    if (JSON.stringify([next, sources]) === scopeKey) return;
    reset();
    const nextParams = withResourceRef(params);

    nextParams.set("scope", next);
    nextParams.delete("source");
    sources.forEach((id) => nextParams.append("source", id));
    try {
      localStorage.setItem("federation.scope", JSON.stringify({ scope: next, sources }));
    } catch {
      /* Browser storage may be disabled. */
    }
    setParams(nextParams);
  };

  const openDetail = (ref?: ResourceRef) => setParams(withResourceRef(params, ref));
  const firstPage = state.pages[0];
  const lastPage = state.pages[state.pages.length - 1];
  const items = state.pages.flatMap((page) => page.items);
  const expired = !!state.deadline && state.deadline <= now;
  const failedNodes = state.error instanceof FederationError ? state.error.omittedNodes : [];
  const refreshRequired =
    state.error instanceof FederationError &&
    [
      "QuerySessionExpired",
      "QuerySessionInterrupted",
      "CursorSuperseded",
      "LibraryEpochChanged",
      "GrantRevoked",
      "InvalidCursor",
    ].includes(state.error.code);
  const sortOptions: CommonLibraryQuery["sort"][] = ["NameAsc", "NameDesc"];
  const availabilityOptions: NonNullable<CommonLibraryQuery["fileAvailability"]>[] = [
    "Any",
    "HasFile",
    "MetadataOnly",
  ];

  return (
    <div className="mx-auto flex max-w-[1600px] flex-col gap-5 p-4 sm:p-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <AiOutlineDatabase aria-hidden />
            {t("federation.title")}
          </h1>
          <p className="mt-2 max-w-3xl text-sm text-default-500">{t("federation.intro")}</p>
        </div>
        <div className="flex gap-2">
          <Link className={buttonClass} to="/resource">
            {t("federation.localLibrary")}
          </Link>
          <Link className={buttonClass} to="/federation/devices">
            {t("federation.devices.title")}
          </Link>
        </div>
      </header>
      <ErrorNotice error={error} onRetry={() => void refresh()} />
      {loading && !status && <p role="status">{t("federation.loading")}</p>}
      {status && status.browsingEnabled !== true && (
        <section className={`${panelClass} space-y-3`}>
          <h2 className="text-lg font-semibold">{t("federation.browsing.off")}</h2>
          <p className="text-sm text-default-500">{t("federation.browsing.description")}</p>
          <ErrorNotice error={enableError} />
          <button
            className={primaryClass}
            disabled={enabling}
            type="button"
            onClick={() =>
              void (async () => {
                setEnabling(true);
                setEnableError(undefined);
                try {
                  await federationPeerApi.browsing(true);
                  await refresh();
                } catch (cause) {
                  setEnableError(cause instanceof Error ? cause : new Error(String(cause)));
                } finally {
                  setEnabling(false);
                }
              })()
            }
          >
            {t("federation.browsing.enable")}
          </button>
        </section>
      )}
      {status?.browsingEnabled === true && (
        <>
          <form
            className={`${panelClass} space-y-4`}
            onSubmit={(event) => {
              event.preventDefault();
              submit();
            }}
          >
            <div
              aria-label={t("federation.scope.label")}
              className="flex flex-wrap items-center gap-2"
              role="group"
            >
              {(["local", "all", "selected"] as Scope[]).map((option) => (
                <button
                  key={option}
                  aria-pressed={scope === option}
                  className={scope === option ? primaryClass : buttonClass}
                  type="button"
                  onClick={() => updateScope(option)}
                >
                  {t(`federation.scope.${option}`)}
                </button>
              ))}
              <span className="ml-auto text-xs text-default-500">{t("federation.readOnly")}</span>
            </div>
            {scope === "selected" && (
              <fieldset className="flex flex-wrap gap-x-4 gap-y-2 rounded-lg bg-default-50 p-3">
                <legend className="sr-only">{t("federation.scope.select")}</legend>
                {sources.map((source) => (
                  <label key={source.nodeId} className="flex items-center gap-2 text-sm">
                    <input
                      checked={selected.includes(source.nodeId)}
                      disabled={!source.available && !selected.includes(source.nodeId)}
                      type="checkbox"
                      onChange={(event) =>
                        updateScope(
                          "selected",
                          event.target.checked
                            ? [...selected, source.nodeId]
                            : selected.filter((id) => id !== source.nodeId),
                        )
                      }
                    />
                    {source.label}
                    {!source.available && (
                      <span className="text-xs text-default-500">
                        {t("federation.notAuthorized")}
                      </span>
                    )}
                  </label>
                ))}
              </fieldset>
            )}
            <div className="grid gap-3 md:grid-cols-[minmax(200px,1fr)_180px_180px_auto]">
              <label className="space-y-1 text-sm">
                <span>{t("federation.search.label")}</span>
                <input
                  className={fieldClass}
                  placeholder={t("federation.search.placeholder")}
                  value={text}
                  onChange={(event) => setText(event.target.value)}
                />
              </label>
              <label className="space-y-1 text-sm">
                <span>{t("federation.availability.label")}</span>
                <select
                  className={fieldClass}
                  value={availability}
                  onChange={(event) => setAvailability(event.target.value as typeof availability)}
                >
                  {availabilityOptions.map((option) => (
                    <option key={option} value={option}>
                      {t(`federation.availability.${option}`)}
                    </option>
                  ))}
                </select>
              </label>
              <label className="space-y-1 text-sm">
                <span>{t("federation.sort.label")}</span>
                <select
                  className={fieldClass}
                  value={sort}
                  onChange={(event) => setSort(event.target.value as typeof sort)}
                >
                  {sortOptions.map((option) => (
                    <option key={option} value={option}>
                      {t(`federation.sort.${option}`)}
                    </option>
                  ))}
                </select>
              </label>
              <button
                className={`${primaryClass} self-end`}
                disabled={!nodeIds.length || state.busy === "preparing"}
                type="submit"
              >
                <AiOutlineSearch aria-hidden />
                {t("federation.search.action")}
              </button>
            </div>
            <details>
              <summary className="cursor-pointer text-xs text-default-500">
                {t("federation.sourceKinds")}
              </summary>
              <div className="mt-2 flex flex-wrap gap-4">
                {resourceSources.map(({ value: kind }) => (
                  <label key={kind} className="flex items-center gap-2 text-sm">
                    <input
                      checked={sourceKinds.includes(kind)}
                      type="checkbox"
                      onChange={(event) =>
                        setSourceKinds(
                          event.target.checked
                            ? [...sourceKinds, kind]
                            : sourceKinds.filter((value) => value !== kind),
                        )
                      }
                    />
                    {t(`federation.source.${kind}`)}
                  </label>
                ))}
              </div>
            </details>
            <p className="text-xs text-default-500">{t("federation.search.tip")}</p>
          </form>
          {state.busy === "preparing" && (
            <div
              className={`${panelClass} flex flex-wrap items-center justify-between gap-3`}
              role="status"
            >
              <div>
                <p>{t("federation.preparing")}</p>
                <p className="mt-1 text-xs text-default-500">
                  {state.requestedNodeIds.map((id) => names.get(id) || id).join(" · ")}
                </p>
              </div>
              <button className={buttonClass} type="button" onClick={cancel}>
                {t("federation.cancel")}
              </button>
            </div>
          )}
          {state.cancelled && (
            <p className="text-sm text-default-500" role="status">
              {t("federation.cancelled")}
            </p>
          )}
          <ErrorNotice
            error={state.error}
            onRetry={() => (firstPage && !expired && !refreshRequired ? void nextPage() : submit())}
          />
          {failedNodes.length > 0 && (
            <ul
              aria-label={t("federation.coverage")}
              className="space-y-1 rounded-xl border border-danger/20 p-4 text-sm"
            >
              {failedNodes.map((node) => (
                <li key={node.nodeId}>
                  {names.get(node.nodeId) || node.nodeId} ·{" "}
                  {t(`federation.error.${node.code}`, { defaultValue: node.code })}
                </li>
              ))}
            </ul>
          )}
          {expired && (
            <div className="rounded-lg bg-warning/10 p-3 text-sm" role="status">
              {t("federation.expired")}{" "}
              <button className="font-medium text-primary underline" type="button" onClick={submit}>
                {t("federation.refreshResults")}
              </button>
            </div>
          )}
          {firstPage && (
            <section
              aria-label={t("federation.coverage")}
              className={`rounded-xl border p-4 ${firstPage.coverageComplete ? "border-default-200 bg-content1" : "border-warning/30 bg-warning/5"}`}
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="font-medium">
                  {t(
                    firstPage.coverageComplete
                      ? "federation.results.complete"
                      : "federation.results.partial",
                    {
                      count: firstPage.totalWithinParticipants,
                      completed: firstPage.participants.length,
                      requested: state.requestedNodeIds.length,
                    },
                  )}
                  {submittedText && (
                    <span className="ml-2 font-normal text-default-500">“{submittedText}”</span>
                  )}
                </p>
                <button
                  className={buttonClass}
                  disabled={!!state.busy}
                  type="button"
                  onClick={submit}
                >
                  {t("federation.refreshResults")}
                </button>
              </div>
              <div className="mt-2 flex flex-wrap gap-2">
                {firstPage.participants.map((node) => (
                  <SourceBadge
                    key={node.nodeId}
                    label={`${names.get(node.nodeId) || node.nodeId} · ${node.totalCount}`}
                    local={node.nodeId === status.identity.nodeId}
                  />
                ))}
              </div>
              {firstPage.omittedNodes.length > 0 && (
                <ul className="mt-3 space-y-1 text-sm">
                  {firstPage.omittedNodes.map((node) => (
                    <li key={node.nodeId}>
                      {names.get(node.nodeId) || node.nodeId} ·{" "}
                      {t(`federation.error.${node.code}`, { defaultValue: node.code })}
                    </li>
                  ))}
                </ul>
              )}
              {!firstPage.coverageComplete && (
                <p className="mt-2 text-xs text-default-500">
                  {t("federation.results.partialTip")}
                </p>
              )}
            </section>
          )}
          {(firstPage || detailRef) && (
            <div
              className={`grid items-start gap-5 ${firstPage && detailRef ? "xl:grid-cols-[minmax(0,1fr)_minmax(320px,440px)]" : ""}`}
            >
              {firstPage && (
                <div className="min-w-0">
                  {!items.length && (
                    <div className={`${panelClass} py-12 text-center`}>
                      <AiOutlineDatabase
                        aria-hidden
                        className="mx-auto mb-3 text-4xl text-default-300"
                      />
                      <h2 className="font-medium">
                        {t(
                          firstPage.coverageComplete
                            ? "federation.empty.title"
                            : "federation.empty.partial",
                        )}
                      </h2>
                      <p className="mx-auto mt-2 max-w-lg text-sm text-default-500">
                        {t(
                          firstPage.coverageComplete
                            ? "federation.empty.tip"
                            : "federation.results.partialTip",
                        )}
                      </p>
                      <Link className={`${buttonClass} mt-4`} to="/federation/devices">
                        {t("federation.devices.add")}
                      </Link>
                    </div>
                  )}
                  <div
                    className={`grid gap-3 ${detailRef ? "sm:grid-cols-2" : "sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4"}`}
                  >
                    {items.map((resource) => (
                      <button
                        key={resourceKey(resource.ref)}
                        className={`${panelClass} flex min-w-0 flex-col gap-3 text-left transition hover:border-primary/60 focus:outline-none focus:ring-2 focus:ring-primary/40`}
                        type="button"
                        onClick={() => openDetail(resource.ref)}
                      >
                        <div className="flex h-20 w-full items-center justify-center rounded-lg bg-gradient-to-br from-primary/5 to-default-100">
                          <AiOutlineFile aria-hidden className="text-3xl text-default-400" />
                        </div>
                        <SourceBadge
                          label={resource.ownerLabel}
                          local={resource.ref.nodeId === status.identity.nodeId}
                        />
                        <h2 className="line-clamp-2 break-words font-medium">
                          {resource.displayName || `#${resource.ref.resourceId}`}
                        </h2>
                        <p className="mt-auto text-xs text-default-500">
                          {t(`federation.availability.${resource.fileAvailability}`)}
                        </p>
                      </button>
                    ))}
                  </div>
                  {lastPage?.nextCursor && !state.cancelled && (
                    <div className="mt-5 text-center">
                      <button
                        className={buttonClass}
                        disabled={!!state.busy || expired}
                        type="button"
                        onClick={() => void nextPage()}
                      >
                        {t(state.busy === "page" ? "federation.loading" : "federation.loadMore")}
                      </button>
                      {state.busy === "page" && (
                        <button className={`${buttonClass} ml-2`} type="button" onClick={cancel}>
                          {t("federation.cancel")}
                        </button>
                      )}
                    </div>
                  )}
                </div>
              )}
              {detailRef && (
                <ResourceDetail
                  key={resourceKey(detailRef)}
                  localEpoch={status.identity.libraryEpoch}
                  localNodeId={status.identity.nodeId}
                  resourceRef={detailRef}
                  onClose={() => openDetail()}
                />
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}
