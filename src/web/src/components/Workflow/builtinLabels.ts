import type { TFunction } from "i18next";

export type WorkflowLabelSource = {
  name: string;
  isBuiltin?: boolean;
};

// AcquisitionRecipeSeeder persists these canonical names and the IsBuiltin flag.
// Database IDs vary by installation; user definitions with the same name are not seeds.
const BUILTIN_NAME_KEYS = new Map([
  ["Forum post + cloud drive", "acquisition.recipe.sharedContent"],
  ["Direct download", "acquisition.recipe.directDownload"],
  ["Magnet", "acquisition.recipe.magnet"],
  ["Magnet download", "acquisition.recipe.magnetDownload"],
  ["Torrent download", "acquisition.recipe.torrent"],
  ["Platform fetch", "acquisition.recipe.platform"],
  ["Local directory", "acquisition.recipe.localDirectory"],
  ["Download torrent contents", "workflow.recipe.downloadTorrentContents.name"],
  ["ExHentai download", "acquisition.recipe.exHentai"],
]);

/** Display only: never use the localized label as a workflow name in API payloads. */
export const workflowLabel = (workflow: WorkflowLabelSource, t: TFunction): string => {
  const key = workflow.isBuiltin ? BUILTIN_NAME_KEYS.get(workflow.name) : undefined;

  return key ? t<string>(key, { defaultValue: workflow.name }) : workflow.name;
};
