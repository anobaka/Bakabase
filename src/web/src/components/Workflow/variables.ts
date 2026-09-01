import type { UpstreamActivityRef } from "./Activities/types";

/**
 * The editor half of the E4 variable soft contract: derive "variables available at this
 * position" from the chain itself, so capture/template forms can hint and lint. Mirrors the
 * backend's WorkflowVariableInterpolator token grammar — keep the two regexes in sync.
 */

const NAMED_GROUP_REGEX = /\(\?<([A-Za-z_][A-Za-z0-9_]*)>/g;
const VAR_TOKEN_REGEX = /\{var:([A-Za-z_][A-Za-z0-9_]*)(?::pad\(\d{1,2}\))?\}/g;

const CAPTURE_KIND = "transform.text.capture";

/** System variables every fs entry answers (FsEntryItem.GetWorkflowSystemVariables). */
export const FS_SYSTEM_VARIABLES = ["extension", "originalName", "parentName"];

/** Named groups a capture pattern would write into the bag. */
export function namedGroupsOf(pattern: string): string[] {
  return Array.from(pattern.matchAll(NAMED_GROUP_REGEX), (m) => m[1]).filter(
    (v, i, arr) => arr.indexOf(v) === i,
  );
}

/** Variables captured by the steps before this position. */
export function upstreamCapturedVariables(upstream: UpstreamActivityRef[] | undefined): string[] {
  const vars: string[] = [];

  for (const step of upstream ?? []) {
    if (step.kind !== CAPTURE_KIND) continue;
    try {
      const pattern = (JSON.parse(step.configJson) as { pattern?: string }).pattern ?? "";

      for (const name of namedGroupsOf(pattern)) {
        if (!vars.includes(name)) vars.push(name);
      }
    } catch {
      // A half-typed upstream config just contributes nothing yet.
    }
  }

  return vars;
}

/** {var:name} tokens referenced by a template (excluding {originalText}). */
export function referencedVariables(template: string): string[] {
  return Array.from(template.matchAll(VAR_TOKEN_REGEX), (m) => m[1]).filter(
    (v, i, arr) => arr.indexOf(v) === i,
  );
}
