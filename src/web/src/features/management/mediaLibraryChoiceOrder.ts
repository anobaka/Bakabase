import type { FilterOwner } from "@/components/ResourceFilter/models";

import { useEffect, useMemo, useState } from "react";

import { getManagementSources } from "./api";
import { mediaLibraryChoiceValue } from "./mediaLibraryFilter";
import { orderMediaLibraryChoices } from "./mediaLibraryOrder";
import { useResourceOwner } from "./resourceContext";
import { useResourceSources } from "./resourceSourcesContext";

import { useUiOptionsStore } from "@/stores/options";

const EmptyOrder: readonly string[] = [];
let localOwnerRequest: Promise<FilterOwner | undefined> | undefined;

function loadLocalOwner() {
  return (localOwnerRequest ??= getManagementSources()
    .then((sources) => sources.find((source) => source.isLocal))
    .finally(() => {
      localOwnerRequest = undefined;
    }));
}

/** Read-only catalog ordering for editors, including portals captured at open time. */
export function createMediaLibraryOrderSource(entries: { value: string; key: string }[]) {
  let currentEntries = entries;
  let previousEntries: typeof entries | undefined;
  const listeners = new Set<() => void>();
  let previous: readonly string[] | undefined;
  let initialized = false;
  let snapshot: string[] = [];

  return {
    subscribe: (changed: () => void) => {
      listeners.add(changed);
      const unsubscribe = useUiOptionsStore.subscribe(changed);

      return () => {
        listeners.delete(changed);
        unsubscribe();
      };
    },
    updateCatalog: (next: typeof entries) => {
      if (JSON.stringify(currentEntries) === JSON.stringify(next)) return;
      currentEntries = next;
      listeners.forEach((changed) => changed());
    },
    getSnapshot: () => {
      const keys = useUiOptionsStore.getState().data.mediaLibraryOrder?.keys;

      if (!initialized || keys !== previous || currentEntries !== previousEntries) {
        initialized = true;
        previous = keys;
        previousEntries = currentEntries;
        snapshot = orderMediaLibraryChoices(
          currentEntries,
          keys ?? EmptyOrder,
          ({ key }) => key,
        ).map(({ value }) => value);
      }

      return snapshot;
    },
  };
}

/** Selections retain their native IDs; only the configured catalog presentation changes. */
export function useConfiguredMediaLibraryChoices<T>(
  choices: T[],
  value: (choice: T) => string,
  options: { owner?: FilterOwner; qualified?: boolean; enabled?: boolean; local?: boolean } = {},
) {
  const { qualified = false, enabled = true } = options;
  const resourceOwner = useResourceOwner();
  const sources = useResourceSources();
  const knownOwner =
    options.owner ??
    (!options.local ? resourceOwner : undefined) ??
    sources?.sources.find((source) => source.isLocal);
  const [localOwner, setLocalOwner] = useState<FilterOwner>();
  const owner = knownOwner ?? localOwner;
  const saved = useUiOptionsStore((store) => store.data.mediaLibraryOrder?.keys);
  const needsLocalOwner = enabled && !qualified && !knownOwner && Boolean(saved?.length);

  useEffect(() => {
    if (!needsLocalOwner) return;
    let canceled = false;

    void loadLocalOwner()
      .then((found) => {
        if (!canceled) setLocalOwner(found);
      })
      .catch(() => {
        // Keep catalog order when identity is unavailable; never borrow another owner's order.
      });

    return () => {
      canceled = true;
    };
  }, [needsLocalOwner]);

  const key = (choice: T) =>
    qualified || !owner ? value(choice) : mediaLibraryChoiceValue(owner, value(choice));
  const entries = choices.map((choice) => ({ value: value(choice), key: key(choice) }));
  const catalogKey = JSON.stringify(entries);
  // Keep a portal connected when an ownerless local editor finishes resolving identity.
  // Explicit owner/catalog changes still produce a new source, preserving detached owner scope.
  const identityKey = JSON.stringify([
    knownOwner?.serverId,
    knownOwner?.libraryEpoch,
    qualified,
    entries.map((entry) => entry.value),
  ]);
  const source = useMemo(() => createMediaLibraryOrderSource(entries), [identityKey]);

  useEffect(() => source.updateCatalog(entries), [source, catalogKey]);

  return {
    choices: enabled ? orderMediaLibraryChoices(choices, saved ?? EmptyOrder, key) : choices,
    source: enabled ? source : undefined,
  };
}
