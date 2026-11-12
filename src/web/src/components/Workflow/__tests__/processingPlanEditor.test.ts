import { describe, expect, it } from "vitest";
import {
  appendProcessingStep,
  canRemoveProcessingStep,
  isCompleteDirectoryRebase,
  readEditableProcessingPlan,
  removeProcessingStep,
  stepInputs,
  updateProcessingStep,
} from "../processingPlanEditor";

describe("lossless processing plan editing", () => {
  it("replaces only incompatible known fields when changing directory operations", () => {
    const plan = readEditableProcessingPlan(
      JSON.stringify({
        requirement: "required",
        steps: [
          {
            id: "a",
            op: "moveFile",
            selector: "*.jpg",
            targetDirectory: "photos",
            future: { keep: true },
          },
        ],
      }),
    )!;
    const rebased = updateProcessingStep(plan, "a", { op: "rebaseDirectory" });
    expect(rebased.steps[0]).toEqual({
      id: "a",
      op: "rebaseDirectory",
      targetDirectory: "photos",
      future: { keep: true },
    });
    const filled = updateProcessingStep(rebased, "a", { sourceDirectory: "outer/photos" });
    expect(isCompleteDirectoryRebase(filled.steps[0])).toBe(true);
    const renamed = updateProcessingStep(filled, "a", { op: "renameFile" });
    expect(renamed.steps[0]).not.toHaveProperty("sourceDirectory");
    expect(renamed.steps[0].future).toEqual({ keep: true });
    expect(plan.steps[0].selector).toBe("*.jpg");
  });

  it("requires an exact source directory and permits an explicit output-root target", () => {
    for (const sourceDirectory of [
      "",
      ".",
      "..",
      "a/../b",
      "/absolute",
      "a//b",
      "*.zip",
      "C:\\files",
    ]) {
      expect(isCompleteDirectoryRebase({ sourceDirectory, targetDirectory: "" })).toBe(false);
    }
    for (const targetDirectory of ["", ".", "photos/new"]) {
      expect(isCompleteDirectoryRebase({ sourceDirectory: "outer/photos", targetDirectory })).toBe(
        true,
      );
    }
    expect(
      isCompleteDirectoryRebase({ sourceDirectory: "outer", targetDirectory: "../escape" }),
    ).toBe(false);
    expect(
      isCompleteDirectoryRebase({ sourceDirectory: "outer", targetDirectory: "", selector: "*" }),
    ).toBe(false);
  });
  it("preserves unknown fields, policies, evidence and multiple input relationships", () => {
    const original = {
      requirement: "required",
      evidence: ["Original post instructions"],
      future: { schema: 4 },
      steps: [
        {
          id: "original-id",
          op: "extractArchive",
          input: "download",
          storagePolicy: { cleanup: "retain" },
          future: [1, 2],
        },
        {
          id: "merged",
          op: "moveFile",
          inputs: ["download", "original-id"],
          input: "preserved-legacy",
          targetDirectory: "output",
        },
      ],
    };
    const plan = readEditableProcessingPlan(JSON.stringify(original))!;
    const next = updateProcessingStep(plan, "original-id", { password: "new" });
    expect(next).toEqual({
      ...original,
      steps: [{ ...original.steps[0], password: "new" }, original.steps[1]],
    });
    expect(stepInputs(next.steps[1])).toEqual(["download", "original-id"]);
    expect(canRemoveProcessingStep(next, "original-id")).toBe(false);
    expect(removeProcessingStep(next, "original-id")).toBe(next);
  });

  it("appends stable unique identifiers without renumbering or retargeting existing steps", () => {
    const plan = readEditableProcessingPlan(
      JSON.stringify({
        requirement: "required",
        steps: [
          { id: "step-2", op: "extractArchive", input: "download" },
          { id: "custom", op: "renameFile", input: "step-2", targetName: "file.zip" },
        ],
      }),
    )!;
    const next = appendProcessingStep(plan, "extractArchive");
    expect(next.steps.slice(0, 2)).toEqual(plan.steps);
    expect(next.steps[2]).toEqual({ id: "step-1", op: "extractArchive", input: "custom" });
    expect(removeProcessingStep(next, "step-1").steps).toEqual(plan.steps);
  });

  it.each([
    "{",
    "null",
    "[]",
    JSON.stringify({ requirement: "required", steps: [{ id: "a", op: "unknownOperation" }] }),
    JSON.stringify({
      requirement: "required",
      steps: [{ id: "a", op: "extractArchive", input: "future" }],
    }),
    JSON.stringify({
      requirement: "required",
      steps: [{ id: "a", op: "extractArchive", inputs: [] }],
    }),
    JSON.stringify({
      requirement: "required",
      steps: [{ id: "a", op: "extractArchive", selector: { dynamic: true } }],
    }),
    JSON.stringify({ requirement: "notRequired", steps: [{ id: "a", op: "extractArchive" }] }),
    JSON.stringify({
      requirement: "required",
      steps: [{ id: "a", op: "rebaseDirectory", sourceDirectory: { unknown: true } }],
    }),
    JSON.stringify({
      requirement: "required",
      steps: [{ id: "a", op: "rebaseDirectory", sourceDirectory: "outer", selector: "*" }],
    }),
    JSON.stringify({
      requirement: "required",
      steps: [{ id: "a", op: "moveFile", sourceDirectory: "outer" }],
    }),
  ])("keeps unsupported or incomplete structures in JSON without rewriting them: %s", (json) => {
    expect(readEditableProcessingPlan(json)).toBeNull();
  });
});
