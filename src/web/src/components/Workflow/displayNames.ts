import type { TFunction } from "i18next";

import { getWorkflowActivityUI } from "./Activities";
import { getWorkflowTriggerUI } from "./Triggers";

/**
 * Resolve a workflow trigger's localized display name. Falls back, in order, to:
 *   1) the i18n value at the UI registry's `displayNameKey`,
 *   2) the server-provided English `displayName`,
 *   3) the raw `kind` string.
 *
 * The server keeps shipping `displayName` (the C# trigger's hard-coded string) as a safety
 * net so an unrecognised kind — e.g. one whose frontend UI hasn't been bundled yet — still
 * renders something meaningful in the editor.
 */
export function triggerDisplayName(
  t: TFunction,
  kind: string,
  fallback?: string | null,
): string {
  const ui = getWorkflowTriggerUI(kind);
  if (ui?.displayNameKey) {
    return t(ui.displayNameKey, { defaultValue: fallback ?? kind });
  }
  return fallback ?? kind;
}

/** Activity counterpart to {@link triggerDisplayName} — same precedence rules. */
export function activityDisplayName(
  t: TFunction,
  kind: string,
  fallback?: string | null,
): string {
  const ui = getWorkflowActivityUI(kind);
  if (ui?.displayName) return ui.displayName;
  if (ui?.displayNameKey) {
    return t(ui.displayNameKey, { defaultValue: fallback ?? kind });
  }
  return fallback ?? kind;
}
