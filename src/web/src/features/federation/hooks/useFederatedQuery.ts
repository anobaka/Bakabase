import type { FederationQueryApi } from "../queryApi";
import type { FederatedQueryPage, LocalFederatedQuery } from "../types";

import { useCallback, useEffect, useRef, useState } from "react";

import { federationQueryApi } from "../queryApi";
import { FederationError, isAbort } from "../transport";

export interface QueryState {
  pages: FederatedQueryPage[];
  busy?: "preparing" | "page";
  error?: Error;
  deadline?: number;
  requestedNodeIds: string[];
  cancelled?: boolean;
}

const initialState: QueryState = { pages: [], requestedNodeIds: [] };

/** The generation belongs to one query snapshot. Late pages never join a different query. */
export function useFederatedQuery(api: FederationQueryApi = federationQueryApi) {
  const [state, setState] = useState<QueryState>(initialState);
  const generation = useRef(0);
  const active = useRef<AbortController>();
  const session = useRef<string>();
  const stateRef = useRef(state);

  stateRef.current = state;

  const release = useCallback(
    (id?: string) => {
      if (id) void api.release(id).catch(() => {});
    },
    [api],
  );

  const cancel = useCallback(() => {
    generation.current += 1;
    active.current?.abort();
    active.current = undefined;
    release(session.current);
    session.current = undefined;
    setState((previous) => ({
      ...previous,
      busy: undefined,
      deadline: undefined,
      cancelled: true,
    }));
  }, [release]);

  useEffect(() => {
    const dispose = () => {
      generation.current += 1;
      active.current?.abort();
      active.current = undefined;
      release(session.current);
      session.current = undefined;
    };
    const restore = (event: PageTransitionEvent) => {
      if (event.persisted)
        setState((previous) => ({
          ...previous,
          busy: undefined,
          deadline: undefined,
          cancelled: true,
        }));
    };

    window.addEventListener("pagehide", dispose);
    window.addEventListener("pageshow", restore);

    return () => {
      window.removeEventListener("pagehide", dispose);
      window.removeEventListener("pageshow", restore);
      dispose();
    };
  }, [release]);

  const search = useCallback(
    async (query: LocalFederatedQuery) => {
      const run = ++generation.current;

      active.current?.abort();
      release(session.current);
      session.current = undefined;
      const controller = new AbortController();

      active.current = controller;
      setState({ pages: [], busy: "preparing", requestedNodeIds: [...query.nodeIds] });

      try {
        const page = await api.create(query, controller.signal);

        if (run !== generation.current) {
          release(page.sessionId);

          return;
        }
        session.current = page.sessionId;
        setState({
          pages: [page],
          deadline: Date.now() + page.expiresInMs,
          requestedNodeIds: [...query.nodeIds],
        });
      } catch (error) {
        if (run === generation.current && !isAbort(error)) {
          setState((previous) => ({ ...previous, busy: undefined, error: asError(error) }));
        }
      } finally {
        if (run === generation.current) active.current = undefined;
      }
    },
    [api, release],
  );

  const nextPage = useCallback(async () => {
    const current = stateRef.current;
    const last = current.pages[current.pages.length - 1];

    if (active.current || !session.current || !last?.nextCursor) return;
    if (current.deadline && current.deadline <= Date.now()) {
      setState((previous) => ({
        ...previous,
        error: new FederationError("QuerySessionExpired", "", 410),
      }));

      return;
    }

    const run = generation.current;
    const controller = new AbortController();

    active.current = controller;
    setState((previous) => ({ ...previous, busy: "page", error: undefined }));
    try {
      const page = await api.page(session.current, last.nextCursor, controller.signal);

      if (run !== generation.current) return;
      setState((previous) => ({
        ...previous,
        pages: [...previous.pages, page],
        deadline: Math.min(previous.deadline ?? Infinity, Date.now() + page.expiresInMs),
        busy: undefined,
      }));
    } catch (error) {
      if (run === generation.current && !isAbort(error)) {
        setState((previous) => ({ ...previous, busy: undefined, error: asError(error) }));
      }
    } finally {
      if (run === generation.current) active.current = undefined;
    }
  }, [api]);

  const reset = useCallback(() => {
    cancel();
    setState(initialState);
  }, [cancel]);

  return { state, search, nextPage, cancel, reset };
}

const asError = (error: unknown) => (error instanceof Error ? error : new Error(String(error)));
