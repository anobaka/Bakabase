import { useState, type Key } from "react";

/** A new request also affects descendants that have not mounted yet. */
export interface DetailExpansionRequest {
  expanded: boolean;
}

/** Local controls can override a parent request until the parent issues another one. */
export function useDetailExpansion(parent?: DetailExpansionRequest, scope?: Key) {
  const [local, setLocal] = useState<{
    parent?: DetailExpansionRequest;
    scope?: Key;
    request: DetailExpansionRequest;
  }>();
  return {
    request: local?.parent === parent && local?.scope === scope ? local?.request : parent,
    setAll: (expanded: boolean) => setLocal({ parent, scope, request: { expanded } }),
  };
}

/** Preserve individual choices through data refreshes; a new bulk request resets them. */
export function useDetailAccordion(keys: Key[], request?: DetailExpansionRequest) {
  const [local, setLocal] = useState<{
    request?: DetailExpansionRequest;
    overrides: Map<string, boolean>;
  }>();
  const overrides = local?.request === request ? local?.overrides : undefined;
  return {
    selectedKeys: new Set(
      keys.filter((key) => overrides?.get(String(key)) ?? request?.expanded ?? false),
    ),
    onSelectionChange: (selection: "all" | Set<Key>) => {
      const selected = selection === "all" ? undefined : new Set([...selection].map(String));
      const next = new Map(overrides);
      for (const key of keys) next.set(String(key), !selected || selected.has(String(key)));
      setLocal({ request, overrides: next });
    },
  };
}

export function useDetailDisclosure(request?: DetailExpansionRequest) {
  const [local, setLocal] = useState<{ request?: DetailExpansionRequest; open: boolean }>();
  const open = local?.request === request ? (local?.open ?? false) : (request?.expanded ?? false);
  return { open, toggle: () => setLocal({ request, open: !open }) };
}
