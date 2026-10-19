import { useState } from "react";

import { mergeMediaLibraryOrder, reorderVisibleMediaLibraries } from "./mediaLibraryOrder";

import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";

const EmptyOrder: string[] = [];
let writes: Promise<unknown> = Promise.resolve();

// Serialize management-page changes against the latest confirmed options so
// toolbar and drag actions cannot overwrite each other with stale full arrays.
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
