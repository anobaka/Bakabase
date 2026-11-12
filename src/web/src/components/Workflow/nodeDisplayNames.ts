import type { TFunction } from "i18next";
import { activityDisplayName } from "./displayNames";

// These are fixed compiler slot IDs, not display names. Match the activity kind as
// well; arbitrary IDs (including nested/user-authored nodes) must remain visible.
const generatedNodeKinds = new Map([
  ["sources.select", "download.selectSources"],
  ["sources.transfer", "baidu.transfer"],
  ["sources.enqueue", "download.enqueueSupported"],
  ["sources.await", "download.awaitCompletion"],
  ["files.continue", "acquisition.plan.continueFiles"],
  ["files.deliver", "fs.publishArtifacts"],
]);

/** A localized activity with a distinguishing ID only when it is not a fixed system slot. */
export function workflowNodeDisplayName(
  t: TFunction,
  node: { kind: string; nodeId?: string | null },
  fallback?: string,
): string {
  const name = activityDisplayName(t, node.kind, fallback);
  return node.nodeId && generatedNodeKinds.get(node.nodeId) !== node.kind
    ? `${name} · ${node.nodeId}`
    : name;
}
