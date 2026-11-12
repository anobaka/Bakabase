import type { PostParserTask } from "@/core/models/PostParserTask";
import type {
  AcquireAdapter,
  AcquireRequest,
  DownloadCandidate,
} from "@/components/Acquire/models";
import type { SourceRef, SourceHistoryFilter } from "@/components/Acquire/sourceHistory";
import { requireIntentResult } from "@/components/Acquire/api";
import BApi from "@/sdk/BApi";
import { getDownloadInfo } from "./results";
import { getDownloadContentGroups } from "./resourceGroups";

/** Parsing execution has its own history; subsequent consumers remain ordinary source runs. */
export const postProcessingHistoryFilter: SourceHistoryFilter = { excludedTriggerKinds: ["postParser.manual"] };

export const postSourceRef = (
  task: Pick<PostParserTask, "id" | "revision">,
  includeRevision = false,
): SourceRef => ({
  module: "postParser",
  entityKind: "task",
  entityId: String(task.id),
  ...(includeRevision ? { revision: String(task.revision ?? 0) } : {}),
});

export function postAcquireInput(task: PostParserTask): {
  purpose: "plan";
  title: string;
  candidates: DownloadCandidate[];
  adapter: AcquireAdapter;
} {
  const info = getDownloadInfo(task);
  const candidates = getDownloadContentGroups(info ?? {}).flatMap((section) =>
    section.resources
      .filter(({ resource }) => !!resource.link?.trim())
      .map(({ resource, sourceIndices }) => ({
        id: String(sourceIndices[0]),
        url: resource.link!.trim(),
        title: section.group?.title,
        groupId: section.group?.id,
        kind: "auto",
        accessCode: resource.code ?? undefined,
        archivePassword: resource.password ?? undefined,
        extractionPlanJson: resource.extraction ? JSON.stringify(resource.extraction) : undefined,
      })),
  );
  const body = (request: AcquireRequest) => {
    const candidateProcessingPlans = Object.fromEntries(
      request.candidates
        .filter(
          (candidate) =>
            candidate.extractionPlanJson !==
            candidates.find((original) => original.id === candidate.id)?.extractionPlanJson,
        )
        .map((candidate) => [candidate.id, candidate.extractionPlanJson ?? ""]),
    );
    return {
      taskId: task.id,
      revision: task.revision ?? 0,
      mode: request.mode,
      title: request.title,
      accountId: request.accountId,
      workflowDefinitionId: request.workflowDefinitionId,
      resourceIndices: request.selectedCandidateIds.map(Number),
      candidateMethods: Object.fromEntries(
        request.candidates
          .filter((candidate) => candidate.methodId)
          .map((candidate) => [candidate.id, candidate.methodId!]),
      ),
      candidateAccounts: Object.fromEntries(
        request.candidates
          .filter((candidate) => candidate.accountId)
          .map((candidate) => [candidate.id, candidate.accountId!]),
      ),
      // The server rereads the saved result. Only explicit edits override its processing plan.
      candidateProcessingPlans,
      candidateSourceCorrections: {},
    };
  };
  return {
    purpose: "plan",
    title: info?.title || task.title || "",
    candidates,
    adapter: {
      preflight: (request, directory) =>
        requireIntentResult(
          BApi.downloadIntents.preflightPostDownloadIntent(body(request), {
            outputDirectory: directory || undefined,
          }),
        ),
      submit: (request, directory) =>
        requireIntentResult(
          BApi.postParser.createPostAcquisitionPlan(body(request), {
            outputDirectory: directory || undefined,
          }),
        ),
    },
  };
}
