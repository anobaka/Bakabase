import type { ActivityDraft } from "./types";

import { getWorkflowActivityUI } from "../Activities";

import { WorkflowActivityErrorBehavior } from "@/sdk/constants";

/**
 * Seeds for the canvas editor: the "file cleaning" one-click template on the workflow page,
 * and the "upgrade to a cleaning workflow" hand-off from the File Name Modifier page
 * (design docs/file-cleaning-workflow.html §6 — the two entry points).
 */

export interface EditorSeed {
  /** i18n key for the suggested workflow name; resolved by the editor. */
  nameKey?: string;
  /** Literal name (wins over nameKey) — used by the File Name Modifier hand-off. */
  name?: string;
  triggerKind: string;
  activities: Array<{ kind: string; configJson?: string }>;
}

/** sessionStorage key the File Name Modifier page writes its hand-off seed into. */
export const EDITOR_SEED_STORAGE_KEY = "bakabase.workflowEditor.seed";

const draftOf = (kind: string, configJson?: string): ActivityDraft => {
  const ui = getWorkflowActivityUI(kind);

  return {
    clientId: crypto.randomUUID(),
    kind,
    configJson: configJson ?? (ui ? ui.serializeConfig(ui.defaultConfig()) : "{}"),
    onItemError: WorkflowActivityErrorBehavior.Fail,
  };
};

export const EDITOR_TEMPLATES: Record<string, EditorSeed> = {
  // Scan → rename ops → trim leftovers → record the plan: the basicClean recipe from the
  // help center, ready to point at a folder.
  fileCleaning: {
    nameKey: "workflow.template.fileCleaning.name",
    triggerKind: "fs.manualScan",
    activities: [
      { kind: "transform.fs.fileNameOp" },
      { kind: "transform.text.trim" },
      { kind: "action.fs.saveName" },
    ],
  },
};

export function seedToDrafts(seed: EditorSeed): ActivityDraft[] {
  return seed.activities.map((a) => draftOf(a.kind, a.configJson));
}

/** Read and consume a one-shot hand-off seed left by another page. */
export function takeStoredSeed(): EditorSeed | null {
  try {
    const raw = sessionStorage.getItem(EDITOR_SEED_STORAGE_KEY);

    if (!raw) return null;
    sessionStorage.removeItem(EDITOR_SEED_STORAGE_KEY);

    return JSON.parse(raw) as EditorSeed;
  } catch {
    return null;
  }
}
