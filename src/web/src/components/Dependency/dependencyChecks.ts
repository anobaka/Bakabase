import { useCallback } from "react";
import { create } from "zustand";

import envConfig from "@/config/env";
import BApi from "@/sdk/BApi";
import { DependentComponentStatus } from "@/sdk/constants";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

export type DependencyVersion = {
  version?: string | null;
  description?: string | null;
  canUpdate: boolean;
  installedVersionRecognized?: boolean;
};
export type DependencyFailure = { stage: "discover" | "latest" | "install"; details: string };
export type DependencyCheckEntry = {
  checking: boolean;
  installRequested: boolean;
  latestVersion?: DependencyVersion;
  failure?: DependencyFailure;
};
export type DependencyCheckEntries = Record<string, DependencyCheckEntry>;
const emptyEntries: DependencyCheckEntries = {};
const emptyEntry: DependencyCheckEntry = { checking: false, installRequested: false };
const quiet = { showErrorToast: false };
const ttl = 5 * 60 * 1000;
const checks = new Map<string, Promise<void>>();
const installs = new Map<string, Promise<void>>();
const discoveries = new Map<string, { generation: number }>();
const checkedAt = new Map<string, number>();
const revisions = new Map<string, number>();
let resetRevision = 0;
let activeChecks = 0;
const waitingChecks: Array<() => Promise<void>> = [];

const useChecksStore = create<{ scopes: Record<string, DependencyCheckEntries> }>(() => ({
  scopes: {},
}));
const scopeKey = () =>
  JSON.stringify([
    envConfig.apiEndpoint || (typeof window !== "undefined" ? window.location.origin : ""),
    useRemoteAccessStore.getState().serverId ?? "",
  ]);
const keyFor = (scope: string, id: string) => JSON.stringify([scope, id]);
const currentContext = (id: string) =>
  useDependentComponentContextsStore.getState().contexts.find((c) => c.id === id);
const entryFor = (scope: string, id: string) =>
  useChecksStore.getState().scopes[scope]?.[id] ?? emptyEntry;
const update = (scope: string, id: string, values: Partial<DependencyCheckEntry>) => {
  const state = useChecksStore.getState();
  const entries = state.scopes[scope] ?? emptyEntries;

  useChecksStore.setState({
    scopes: {
      ...state.scopes,
      [scope]: { ...entries, [id]: { ...entryFor(scope, id), ...values } },
    },
  });
};
const invalidate = (scope: string, id: string) => {
  const key = keyFor(scope, id);

  checkedAt.delete(key);
  revisions.set(key, (revisions.get(key) ?? 0) + 1);
};
const errorText = (error: unknown): string => {
  if (typeof error === "string") return error;
  if (error instanceof Error) return error.stack || error.message;
  if (error && typeof error === "object") {
    if ("error" in error && error.error) return errorText(error.error);
    if ("message" in error && typeof error.message === "string") return error.message;
  }

  return String(error);
};

// Every automatic card check and explicit batch uses this same three-slot scheduler.
const pump = () => {
  while (activeChecks < 3 && waitingChecks.length) {
    activeChecks++;
    void waitingChecks.shift()!();
  }
};
const schedule = (run: () => Promise<void>) =>
  new Promise<void>((resolve) => {
    waitingChecks.push(async () => {
      try {
        await run();
      } finally {
        activeChecks--;
        resolve();
        pump();
      }
    });
    pump();
  });

const checkDependency = (
  scope: string,
  id: string,
  rediscover = false,
  force = false,
): Promise<void> => {
  const key = keyFor(scope, id);
  const pending = checks.get(key);

  if (pending) return pending;
  const entry = entryFor(scope, id);
  const context = currentContext(id);

  if (
    scope !== scopeKey() ||
    context?.isAvailableOnCurrentPlatform === false ||
    context?.status === DependentComponentStatus.Installing ||
    entry.installRequested
  )
    return Promise.resolve();
  const timestamp = checkedAt.get(key);

  if (!force && timestamp !== undefined && Date.now() - timestamp < ttl) return Promise.resolve();
  const generation = resetRevision;
  const revision = revisions.get(key) ?? 0;

  update(scope, id, { checking: true });
  const active = () =>
    generation === resetRevision && revision === (revisions.get(key) ?? 0) && scope === scopeKey();
  const operation = schedule(async () => {
    let stage: DependencyFailure["stage"] = rediscover ? "discover" : "latest";

    try {
      if (
        !active() ||
        currentContext(id)?.isAvailableOnCurrentPlatform === false ||
        currentContext(id)?.status === DependentComponentStatus.Installing
      )
        return;
      if (rediscover) {
        const discovery = { generation };
        discoveries.set(key, discovery);
        let response: Awaited<ReturnType<typeof BApi.component.discoverDependentComponent>>;

        try {
          response = await BApi.component.discoverDependentComponent(id, quiet);
        } finally {
          if (discoveries.get(key) === discovery) discoveries.delete(key);
        }

        if (!active()) return;
        if (response.code) throw response;
        if (currentContext(id)?.isAvailableOnCurrentPlatform === false) return;
      }
      stage = "latest";
      const response = await BApi.component.getDependentComponentLatestVersion(id, quiet);

      if (!active()) return;
      if (response.code) throw response;
      const previous = entryFor(scope, id).failure;

      update(scope, id, {
        latestVersion: response.data ?? { canUpdate: false },
        failure: !rediscover && previous?.stage === "install" ? previous : undefined,
      });
      checkedAt.set(key, Date.now());
    } catch (error) {
      if (active()) {
        const previous = entryFor(scope, id).failure;

        update(scope, id, {
          failure:
            !rediscover && previous?.stage === "install"
              ? previous
              : { stage, details: errorText(error) },
        });
        checkedAt.set(key, Date.now());
      }
    }
  }).finally(() => {
    if (generation !== resetRevision) return;
    checks.delete(key);
    update(scope, id, { checking: false });
    // Installation or a source change can invalidate an older lookup while it is still in flight.
    if (scope === scopeKey() && revision !== (revisions.get(key) ?? 0))
      void checkDependency(scope, id, false, true);
  });

  checks.set(key, operation);

  return operation;
};

const installDependency = (scope: string, id: string): Promise<void> => {
  const key = keyFor(scope, id);
  const pending = installs.get(key);

  if (pending) return pending;
  if (
    scope !== scopeKey() ||
    entryFor(scope, id).checking ||
    currentContext(id)?.isAvailableOnCurrentPlatform === false ||
    currentContext(id)?.status === DependentComponentStatus.Installing
  )
    return Promise.resolve();
  const generation = resetRevision;

  invalidate(scope, id);
  update(scope, id, { installRequested: true, failure: undefined });
  const operation = (async () => {
    try {
      const response = await BApi.component.installDependentComponent(id, quiet);

      if (response.code) throw response;
    } catch (error) {
      if (generation === resetRevision)
        update(scope, id, { failure: { stage: "install", details: errorText(error) } });
    } finally {
      if (generation === resetRevision) {
        installs.delete(key);
        update(scope, id, { installRequested: false });
        invalidate(scope, id);
        await checkDependency(scope, id, false, true);
      }
    }
  })();

  installs.set(key, operation);

  return operation;
};

// Broadcast changes also refresh installs and runtime sources changed by another client.
useDependentComponentContextsStore.subscribe((state, previous) => {
  const scope = scopeKey();

  for (const context of state.contexts) {
    const prior = previous.contexts.find((c) => c.id === context.id);

    if (
      context.error &&
      context.status !== DependentComponentStatus.Installing &&
      entryFor(scope, context.id).failure?.details !== context.error
    )
      update(scope, context.id, { failure: { stage: "install", details: context.error } });
    if (
      context.status === DependentComponentStatus.Installing &&
      prior?.status !== context.status
    ) {
      invalidate(scope, context.id);
      update(scope, context.id, { failure: undefined });
    }
    const sourceChanged =
      prior &&
      (prior.version !== context.version ||
        (prior.location ?? prior.defaultLocation) !==
          (context.location ?? context.defaultLocation) ||
        prior.isAvailableOnCurrentPlatform !== context.isAvailableOnCurrentPlatform ||
        (prior.canAutoInstallOnCurrentPlatform ?? true) !==
          (context.canAutoInstallOnCurrentPlatform ?? true));
    // Discovery can publish the new version/location itself. Its following latest lookup
    // already uses that source; invalidating it would lose an explicit retry's result.
    const discovering = discoveries.get(keyFor(scope, context.id))?.generation === resetRevision;

    if (
      context.status !== DependentComponentStatus.Installing &&
      (prior?.status === DependentComponentStatus.Installing || (sourceChanged && !discovering))
    ) {
      invalidate(scope, context.id);
      void checkDependency(scope, context.id, false, true);
    }
  }
});

export const useDependencyChecks = () => {
  const serverId = useRemoteAccessStore((state) => state.serverId);
  const scope = JSON.stringify([
    envConfig.apiEndpoint || (typeof window !== "undefined" ? window.location.origin : ""),
    serverId ?? "",
  ]);
  const entries = useChecksStore((state) => state.scopes[scope] ?? emptyEntries);
  const check = useCallback(
    (id: string, rediscover = false, force = false) =>
      checkDependency(scope, id, rediscover, force),
    [scope],
  );
  const checkMany = useCallback(
    async (ids: string[]) => {
      await Promise.all([...new Set(ids)].map((id) => checkDependency(scope, id, true, true)));
    },
    [scope],
  );
  const install = useCallback((id: string) => installDependency(scope, id), [scope]);

  return {
    entries,
    checking: Object.values(entries).some((entry) => entry.checking),
    checkDependency: check,
    checkDependencies: checkMany,
    installDependency: install,
  };
};

/** Clears only the ephemeral lookup cache; useful for isolated tests and explicit session teardown. */
export const resetDependencyChecks = () => {
  resetRevision++;
  checkedAt.clear();
  revisions.clear();
  checks.clear();
  installs.clear();
  discoveries.clear();
  useChecksStore.setState({ scopes: {} });
};
