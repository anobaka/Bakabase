import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionPlan as Plan } from "@/sdk/Api";
import type { AcquireRequest } from "../models";

/** Explicitly selected independent sources become parts; no mirror relationship or extraction is inferred. */
export function draftPlanFromRequest(
  request: AcquireRequest,
  directory: string,
  title: string,
): Plan {
  const selected = request.candidates.filter((candidate) =>
    request.selectedCandidateIds.includes(candidate.id),
  );
  if (!selected.length || selected.length !== request.selectedCandidateIds.length)
    throw new Error("SELECTION_INVALID");
  return {
    version: 1,
    adaptiveFileProcessing: false,
    adaptiveFileProcessingMaxIterations: 3,
    publishRetainedFiles: false,
    coverCandidates: [],
    title: request.title?.trim() || title,
    outputDirectory: directory,
    delivery: "files",
    parts: selected.map((candidate, index) => ({
      id: `part-${index + 1}`,
      title: candidate.title,
      selectedSourceId: candidate.id,
      sources: [
        {
          id: candidate.id,
          url: candidate.url,
          kind: candidate.kind,
          providerId: candidate.providerId,
          methodId: candidate.methodId,
          accountId: candidate.accountId,
          accessCode: candidate.accessCode,
          archivePassword: candidate.archivePassword,
        },
      ],
      processing: candidate.extractionPlanJson?.trim()
        ? JSON.parse(candidate.extractionPlanJson)
        : { requirement: "unknown", steps: [], evidence: [] },
    })),
  };
}
