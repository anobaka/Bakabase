import type { ReactNode } from "react";

import { useRef } from "react";

import { useEscapeKey } from "@/features/federation/map/useEscapeKey";

/**
 * Details that close on Escape the way the `/data-sync` page's and the device map's do: a
 * listener on their own element (`useEscapeKey`), which an Escape reaches before any React
 * handler inside them.
 */
export default function EscapableDetails({
  onEscape,
  children,
}: {
  onEscape: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLElement>(null);

  useEscapeKey(ref, onEscape);

  return (
    <aside ref={ref} data-testid="escapable-details">
      {children}
    </aside>
  );
}
