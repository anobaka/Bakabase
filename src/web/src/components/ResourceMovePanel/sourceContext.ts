import type { MoveSourceContext } from "./types";

export function isMoveSourceContext(value: unknown): value is MoveSourceContext {
  if (!value || typeof value !== "object") return false;
  const context = value as MoveSourceContext;

  return (
    typeof context.nodeId === "string" &&
    !!context.nodeId &&
    typeof context.libraryEpoch === "string" &&
    !!context.libraryEpoch
  );
}

/** Resource IDs only have meaning within one node and library generation. */
export function moveSourceContextError(
  source?: MoveSourceContext,
  current?: MoveSourceContext,
): string | undefined {
  if (!isMoveSourceContext(source) || !isMoveSourceContext(current)) return "sourceContextRequired";
  if (source.nodeId !== current.nodeId) return "foreignMoveSource";
  if (source.libraryEpoch !== current.libraryEpoch) return "sourceContextChanged";

  return undefined;
}
