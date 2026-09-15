import type { components } from "@/sdk/BApi2";
import type { WorkflowValidation } from "./metadata";

import { useEffect, useRef, useState, useSyncExternalStore } from "react";

import BApi from "@/sdk/BApi";
import { optionsStores } from "@/stores/options";

type Definition =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];
type Draft = Parameters<typeof BApi.workflow.validateWorkflow>[0];
export type WorkflowCheckState = {
  loading?: boolean;
  failed?: boolean;
  result?: WorkflowValidation;
};
type CachedCheck = { state: WorkflowCheckState; checkedAt: number };
const CHECK_TTL = 60_000;
const CONCURRENCY = 3;
const cache = new Map<string, CachedCheck>();
let nextOptionIdentity = 0;
const optionRevisions = new Map<object, { data: object; serialized: string; revision: number }>();

// Presentation preferences cannot change workflow validation. Other settings may supply
// credentials, directories or providers. Repeated snapshots with unchanged values should
// not invalidate checks; only opaque revision numbers enter the validation cache key.
const validationStores = Object.entries(optionsStores)
  .filter(([name]) => name !== "uiOptions" && name !== "uiStyleOptions")
  .map(([, store]) => store);
const configurationSnapshot = () =>
  validationStores
    .map((store) => {
      const data = store.getState().data;
      const previous = optionRevisions.get(store);

      if (previous && previous.data === data) return previous.revision;
      const serialized = JSON.stringify(data);
      const revision =
        previous && previous.serialized === serialized ? previous.revision : ++nextOptionIdentity;

      optionRevisions.set(store, { data, serialized, revision });

      return revision;
    })
    .join(":");
const subscribeConfiguration = (onChange: () => void) => {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const subscriptions = validationStores.map((store) =>
    store.subscribe((state, previous) => {
      if (state.data === previous.data) return;
      clearTimeout(timer);
      timer = setTimeout(onChange, 200);
    }),
  );

  return () => {
    clearTimeout(timer);
    subscriptions.forEach((unsubscribe) => unsubscribe());
  };
};

function useConfigurationSnapshot() {
  return useSyncExternalStore(subscribeConfiguration, configurationSnapshot);
}
function definitionKey(workflow: Definition, configuration: string) {
  return JSON.stringify([
    BApi.baseUrl,
    configuration,
    workflow.id,
    workflow.updatedAt,
    workflow.triggerKind,
    workflow.triggerFilterJson,
    workflow.enabled,
    workflow.activities,
  ]);
}
function remember(key: string, state: WorkflowCheckState) {
  cache.delete(key);
  cache.set(key, { state, checkedAt: Date.now() });
  if (cache.size > 200) cache.delete(cache.keys().next().value!);
}

/** Read-only configuration checks: bounded concurrency, short cache, no workflow execution. */
export function useSavedWorkflowValidation(workflows: Definition[]) {
  const configuration = useConfigurationSnapshot();
  const keys = new Map(
    workflows.map((workflow) => [workflow.id, definitionKey(workflow, configuration)]),
  );
  const signature = JSON.stringify([...keys]);
  const [states, setStates] = useState<Record<number, { key: string; state: WorkflowCheckState }>>(
    {},
  );
  const retryRef = useRef<(id: number) => void>(() => {});

  useEffect(() => {
    let active = true;
    let running = 0;
    const queue: number[] = [];
    const pending = new Set<number>();
    const controllers = new Set<AbortController>();
    const publish = (id: number, state: WorkflowCheckState) => {
      if (active) setStates((previous) => ({ ...previous, [id]: { key: keys.get(id)!, state } }));
    };
    const pump = () => {
      while (active && running < CONCURRENCY && queue.length) {
        const id = queue.shift()!;
        const key = keys.get(id)!;
        const controller = new AbortController();

        controllers.add(controller);
        running += 1;
        void (async () => {
          try {
            const response = await BApi.workflow.validateSavedWorkflow(id, {
              signal: controller.signal,
              showErrorToast: false,
            });

            if (!active || controller.signal.aborted) return;
            if (response.code || !response.data) throw new Error("Workflow validation unavailable");
            const state = { result: response.data };

            remember(key, state);
            publish(id, state);
          } catch {
            if (active && !controller.signal.aborted) {
              const state = { failed: true };

              remember(key, state);
              publish(id, state);
            }
          } finally {
            controllers.delete(controller);
            pending.delete(id);
            running -= 1;
            pump();
          }
        })();
      }
    };
    const schedule = (id: number, force = false) => {
      if (!active || pending.has(id) || !keys.has(id)) return;
      const key = keys.get(id)!;
      const cached = cache.get(key);

      if (!force && cached && Date.now() - cached.checkedAt < CHECK_TTL) {
        publish(id, cached.state);

        return;
      }
      pending.add(id);
      publish(id, { loading: true });
      queue.push(id);
      pump();
    };
    const refreshExpired = () => {
      if (document.visibilityState === "hidden") return;
      keys.forEach((_, id) => schedule(id));
    };

    retryRef.current = (id) => schedule(id, true);
    // Provider/feature configuration may live outside options stores. A fresh page or
    // changed definition always checks again; the cache only suppresses focus refreshes.
    keys.forEach((_, id) => schedule(id, true));
    window.addEventListener("focus", refreshExpired);
    document.addEventListener("visibilitychange", refreshExpired);

    return () => {
      active = false;
      retryRef.current = () => {};
      controllers.forEach((controller) => controller.abort());
      window.removeEventListener("focus", refreshExpired);
      document.removeEventListener("visibilitychange", refreshExpired);
    };
  }, [signature]);

  return {
    getState: (id: number): WorkflowCheckState =>
      states[id] && states[id].key === keys.get(id) ? states[id].state : { loading: true },
    retry: (id: number) => retryRef.current(id),
  };
}

/** Validate the current draft after editing settles; late responses never describe newer edits. */
export function useDraftWorkflowValidation(draft: Draft) {
  const configuration = useConfigurationSnapshot();
  const [retryCount, setRetryCount] = useState(0);
  const key = JSON.stringify([BApi.baseUrl, configuration, draft, retryCount]);
  const [state, setState] = useState<{ key: string; value: WorkflowCheckState }>();
  const checkedAt = useRef(0);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      void (async () => {
        try {
          const response = await BApi.workflow.validateWorkflow(draft, {
            signal: controller.signal,
            showErrorToast: false,
          });

          if (!active) return;
          if (response.code || !response.data) throw new Error("Workflow validation unavailable");
          checkedAt.current = Date.now();
          setState({ key, value: { result: response.data } });
        } catch {
          if (active) {
            checkedAt.current = Date.now();
            setState({ key, value: { failed: true } });
          }
        }
      })();
    }, 500);

    return () => {
      active = false;
      clearTimeout(timer);
      controller.abort();
    };
  }, [key]);
  useEffect(() => {
    const refreshExpired = () => {
      if (
        document.visibilityState !== "hidden" &&
        checkedAt.current &&
        Date.now() - checkedAt.current >= CHECK_TTL
      ) {
        checkedAt.current = 0;
        setRetryCount((count) => count + 1);
      }
    };

    window.addEventListener("focus", refreshExpired);
    document.addEventListener("visibilitychange", refreshExpired);

    return () => {
      window.removeEventListener("focus", refreshExpired);
      document.removeEventListener("visibilitychange", refreshExpired);
    };
  }, []);

  return {
    ...(state?.key === key ? state.value : { loading: true }),
    retry: () => setRetryCount((count) => count + 1),
  };
}
