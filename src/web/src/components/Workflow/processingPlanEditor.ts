import { isExtractionLayout, type ExtractionLayout } from "./extractionLayout";
import type { ArchiveOpenMode, ZipFileNameCodePage, ZipPasswordCodePage } from "@/sdk/constants";
import { isZipFileNameCodePage } from "./zipFileNameCodePage";
import { isZipPasswordCodePage } from "./zipPasswordCodePage";
import { isArchiveOpenMode } from "./archiveOpenMode";

export const processingOperations = [
  "extractArchive",
  "renameExtension",
  "renameFile",
  "renameLiteral",
  "moveFile",
  "rebaseDirectory",
] as const;
export type ProcessingOperation = (typeof processingOperations)[number];
export type EditableProcessingStep = Record<string, unknown> & {
  id: string;
  op: ProcessingOperation;
  input?: string;
  inputs?: string[] | null;
  extractionLayout?: ExtractionLayout | null;
  zipFileNameCodePage?: ZipFileNameCodePage | null;
  zipPasswordCodePage?: ZipPasswordCodePage | null;
  archiveOpenMode?: ArchiveOpenMode | null;
  archiveMemberSelector?: string | null;
};
export type EditableProcessingPlan = Record<string, unknown> & {
  requirement: "required" | "notRequired" | "unknown";
  steps: EditableProcessingStep[];
};

const record = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === "object" && !Array.isArray(value);
export const stepInputs = (step: EditableProcessingStep): string[] =>
  step.inputs ?? [step.input ?? "download"];

/** Only represent structures the form understands. Unrecognized data stays in JSON, untouched. */
export function readEditableProcessingPlan(json: string): EditableProcessingPlan | null {
  if (!json.trim()) return { requirement: "unknown", steps: [], evidence: [] };
  try {
    const value: unknown = JSON.parse(json);
    if (
      !record(value) ||
      !["required", "notRequired", "unknown"].includes(String(value.requirement)) ||
      !Array.isArray(value.steps) ||
      (value.requirement === "notRequired" && value.steps.length > 0)
    )
      return null;
    const seen = new Set(["download"]);
    for (const step of value.steps) {
      if (
        !record(step) ||
        typeof step.id !== "string" ||
        !step.id ||
        seen.has(step.id) ||
        !processingOperations.includes(step.op as ProcessingOperation) ||
        (step.input !== undefined && typeof step.input !== "string") ||
        (step.inputs != null &&
          (!Array.isArray(step.inputs) ||
            step.inputs.length === 0 ||
            step.inputs.some((input) => typeof input !== "string")))
      )
        return null;
      if (
        [
          "selector",
          "extension",
          "password",
          "targetName",
          "targetDirectory",
          "sourceDirectory",
          "findText",
          "replaceText",
          "archiveMemberSelector",
        ].some((key) => step[key] != null && typeof step[key] !== "string")
      )
        return null;
      if (
        (step.op === "rebaseDirectory" && step.selector != null && step.selector !== "") ||
        (step.op !== "rebaseDirectory" && step.sourceDirectory != null) ||
        (step.extractionLayout != null &&
          (step.op !== "extractArchive" || !isExtractionLayout(step.extractionLayout))) ||
        (step.zipFileNameCodePage != null &&
          (step.op !== "extractArchive" || !isZipFileNameCodePage(step.zipFileNameCodePage))) ||
        (step.zipPasswordCodePage != null &&
          (step.op !== "extractArchive" || !isZipPasswordCodePage(step.zipPasswordCodePage))) ||
        (step.archiveOpenMode != null &&
          (step.op !== "extractArchive" || !isArchiveOpenMode(step.archiveOpenMode))) ||
        (step.archiveMemberSelector != null && step.archiveOpenMode == null) ||
        (step.archiveOpenMode != null &&
          (step.zipFileNameCodePage != null || step.zipPasswordCodePage != null))
      )
        return null;
      if (stepInputs(step as EditableProcessingStep).some((id) => !seen.has(id))) return null;
      seen.add(step.id);
    }
    return value as EditableProcessingPlan;
  } catch {
    return null;
  }
}

/** Copy only the edited property; preserve evidence, policies, unknown fields and existing identities. */
export function updateProcessingStep(
  plan: EditableProcessingPlan,
  id: string,
  patch: Record<string, unknown>,
): EditableProcessingPlan {
  return {
    ...plan,
    steps: plan.steps.map((step) => {
      if (step.id !== id) return step;
      const next = { ...step, ...patch };
      if (patch.op && patch.op !== step.op) {
        if (patch.op === "renameLiteral") next.replaceText ??= "";
        if (step.op === "extractArchive") {
          delete next.extractionLayout;
          delete next.zipFileNameCodePage;
          delete next.zipPasswordCodePage;
          delete next.archiveOpenMode;
          delete next.archiveMemberSelector;
        }
        if (patch.op === "rebaseDirectory") {
          delete next.selector;
          next.targetDirectory ??= "";
        } else if (step.op === "rebaseDirectory") delete next.sourceDirectory;
      }
      if ("archiveOpenMode" in patch) {
        if (patch.archiveOpenMode == null) delete next.archiveMemberSelector;
        else {
          delete next.zipFileNameCodePage;
          delete next.zipPasswordCodePage;
        }
      }
      return next;
    }),
  };
}
export function appendProcessingStep(
  plan: EditableProcessingPlan,
  op: ProcessingOperation,
): EditableProcessingPlan {
  const used = new Set(plan.steps.map((step) => step.id));
  let index = 1;
  while (used.has(`step-${index}`)) index++;
  return {
    ...plan,
    requirement: "required",
    steps: [
      ...plan.steps,
      {
        id: `step-${index}`,
        op,
        input: plan.steps.at(-1)?.id ?? "download",
        ...(op === "renameLiteral" ? { replaceText: "" } : {}),
        ...(op === "rebaseDirectory" ? { sourceDirectory: "", targetDirectory: "" } : {}),
      },
    ],
  };
}

/** Form completeness only. The server still validates paths, input membership and collisions. */
export function isCompleteDirectoryRebase(step: Record<string, unknown>): boolean {
  const relative = (value: unknown) =>
    typeof value === "string" &&
    value.length > 0 &&
    value.length <= 512 &&
    value
      .replace(/\\/g, "/")
      .split("/")
      .every(
        (part) =>
          part.trim().length > 0 &&
          part !== "." &&
          part !== ".." &&
          !Array.from(part).some(
            (character) => character.charCodeAt(0) < 32 || '<>:"|?*'.includes(character),
          ),
      );
  return (
    relative(step.sourceDirectory) &&
    (step.targetDirectory === "" ||
      step.targetDirectory === "." ||
      relative(step.targetDirectory)) &&
    (step.selector == null || step.selector === "")
  );
}
export const canRemoveProcessingStep = (plan: EditableProcessingPlan, id: string): boolean =>
  !plan.steps.some((step) => step.id !== id && stepInputs(step).includes(id));
export function removeProcessingStep(
  plan: EditableProcessingPlan,
  id: string,
): EditableProcessingPlan {
  return canRemoveProcessingStep(plan, id)
    ? { ...plan, steps: plan.steps.filter((step) => step.id !== id) }
    : plan;
}
