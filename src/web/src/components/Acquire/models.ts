import type {
  BakabaseServiceComponentsDownloadIntentsDownloadCandidate,
  BakabaseServiceComponentsDownloadIntentsDownloadRequirement,
  BakabaseServiceComponentsDownloadIntentsDownloadMethodView,
  BakabaseServiceComponentsDownloadIntentsDownloadCandidateView,
  BakabaseServiceComponentsDownloadIntentsDownloadIntentRequest,
  BakabaseServiceComponentsDownloadIntentsDownloadIntentPreflight,
  BakabaseServiceComponentsDownloadIntentsDownloadIntentSubmission,
} from "@/sdk/Api";

export type AcquireMode = "files" | "library" | "lead";
export type DownloadCandidate = BakabaseServiceComponentsDownloadIntentsDownloadCandidate;
export type DownloadRequirement = BakabaseServiceComponentsDownloadIntentsDownloadRequirement;
export type DownloadMethod = BakabaseServiceComponentsDownloadIntentsDownloadMethodView;
export type PreparedCandidate = BakabaseServiceComponentsDownloadIntentsDownloadCandidateView;
export type AcquireRequest = Omit<
  BakabaseServiceComponentsDownloadIntentsDownloadIntentRequest,
  "mode"
> & { mode: AcquireMode };
export type AcquirePreflight = BakabaseServiceComponentsDownloadIntentsDownloadIntentPreflight;
export type AcquireSubmission = BakabaseServiceComponentsDownloadIntentsDownloadIntentSubmission;
export interface AcquireAdapter {
  preflight: (request: AcquireRequest, outputDirectory: string) => Promise<AcquirePreflight>;
  submit: (request: AcquireRequest, outputDirectory: string) => Promise<AcquireSubmission>;
}

/** A declared group is a mirror group; unrelated/ungrouped URLs stay independent. */
export function toggleCandidateSelection(
  selected: string[],
  candidate: DownloadCandidate,
  candidates: DownloadCandidate[],
  checked: boolean,
  mode: AcquireMode,
) {
  if (!checked) return selected.filter((id) => id !== candidate.id);
  if (mode === "lead") return [...new Set([...selected, candidate.id])];
  const siblings = new Set(
    candidates
      .filter((item) => candidate.groupId && item.groupId === candidate.groupId)
      .map((item) => item.id),
  );
  return [...selected.filter((id) => !siblings.has(id) && id !== candidate.id), candidate.id];
}
