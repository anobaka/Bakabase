import { useState } from "react";

import {
  mergeMediaLibraryOrder,
  orderMediaLibraryChoices,
  reorderVisibleMediaLibraries,
} from "./mediaLibraryOrder";

import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";

const EmptyOrder: string[] = [];
let writes: Promise<unknown> = Promise.resolve();

/** Portals keep the catalog captured at open time but subscribe to confirmed shared ordering. */
export function createMediaLibraryOrderSource(entries: { value: string; key: string }[]) {
  let previous: readonly string[] | undefined;
  let initialized = false;
  let snapshot: string[] = [];

  return {
    subscribe: (changed: () => void) => useUiOptionsStore.subscribe(changed),
    getSnapshot: () => {
      const keys = useUiOptionsStore.getState().data.mediaLibraryOrder?.keys;

      if (!initialized || keys !== previous) {
        initialized = true;
        previous = keys;
        snapshot = orderMediaLibraryChoices(entries, keys ?? EmptyOrder, ({ key }) => key).map(
          ({ value }) => value,
        );
      }

      return snapshot;
    },
  };
}

// Several resource tabs/editors can be mounted at once. Compute each write from the
// latest confirmed options after the previous request, so their full arrays cannot race.
function saveOrder(knownKeys: string[], change: (current: string[]) => string[]) {
  const write = writes.then(async () => {
    try {
      const current = mergeMediaLibraryOrder(
        useUiOptionsStore.getState().data.mediaLibraryOrder?.keys ?? [],
        knownKeys,
      );
      const payload = { keys: change(current) };
      const response = await BApi.options.patchUiOptions({ mediaLibraryOrder: payload });

      if (response.code) return false;
      useUiOptionsStore.getState().update({ mediaLibraryOrder: payload });

      return true;
    } catch {
      return false;
    }
  });

  writes = write;

  return write;
}

/** One install-wide presentation preference, shared by management cards and resource filters. */
export function useMediaLibraryOrder(knownKeys: string[]) {
  const keys = useUiOptionsStore((store) => store.data.mediaLibraryOrder?.keys ?? EmptyOrder);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(false);
  const save = async (change: (current: string[]) => string[]) => {
    setSaving(true);
    setError(false);
    try {
      const success = await saveOrder(knownKeys, change);

      setError(!success);

      return success;
    } finally {
      setSaving(false);
    }
  };
  const arrange = (visible: string[]) => {
    const shown = new Set(visible);
    let index = 0;

    return save((current) => current.map((key) => (shown.has(key) ? visible[index++]! : key)));
  };

  return {
    keys,
    saving,
    error,
    arrange,
    move: (active: string, over: string, visible: string[]) =>
      save((current) => reorderVisibleMediaLibraries(current, visible, active, over)),
  };
}
