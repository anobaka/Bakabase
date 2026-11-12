import { describe, expect, it } from "vitest";

import { localFileWorkflowPreview } from "../localProcessingPlanPreview";

const node = (kind: string, config: unknown = {}, order = 0) => ({
  id: order + 1,
  kind,
  order,
  configJson: JSON.stringify(config),
  onItemError: 1 as const,
  parameterBindings: [],
});
const complete = { requirement: "required", steps: [{ op: "extractArchive" }] };
const notices = (kind: string, config: unknown, plan = complete, done = false) =>
  localFileWorkflowPreview({ activities: [node(kind, config)] }, plan, done)[0].notices.map(
    (notice) => notice.key,
  );

describe("read-only local processing preview", () => {
  it("shows exactly the configured nodes in execution order without inventing acquisition, delivery or cleanup", () => {
    const activities = [node("acquisition.unpack", {}, 2), node("extension.inspect", {}, 1)];
    const result = localFileWorkflowPreview({ activities }, complete);
    expect(result.map((step) => step.activity.kind)).toEqual([
      "extension.inspect",
      "acquisition.unpack",
    ]);
    expect(result[0].notices).toEqual([]);
    expect(result[1].notices.map((notice) => notice.key)).toEqual([
      "savedInstructions",
      "unpackWorkingCopy",
    ]);
    expect(activities[0].kind).toBe("acquisition.unpack");
  });

  it.each([
    undefined,
    { requirement: "unknown", steps: [] },
    { requirement: "required", steps: [] },
    { requirement: "unexpected", steps: [] },
  ])("makes incomplete instructions wait instead of promising success: %j", (plan) => {
    const result = localFileWorkflowPreview(
      {
        activities: [
          node("acquisition.unpack", { allowAutomaticDetection: true, deleteArchive: true }),
        ],
      },
      plan,
    );
    expect(result[0].notices.map((notice) => notice.key)).toEqual([
      "waitForInstructions",
      "unpackWorkingCopy",
    ]);
  });

  it("marks already-processed and no-processing-required inputs explicitly", () => {
    expect(notices("acquisition.unpack", {}, complete, true)).toContain("alreadyProcessed");
    expect(notices("acquisition.unpack", {}, { requirement: "notRequired", steps: [] })).toContain(
      "notRequired",
    );
  });

  it("does not mistake recursive permission for initial archive-processing approval", () => {
    const result = localFileWorkflowPreview(
      { activities: [node("fs.processArtifacts", { recursive: { enabled: true } })] },
      { requirement: "required", steps: [] },
    );
    expect(result[0].notices.map((notice) => notice.key)).toEqual([
      "waitForInstructions",
      "recursive",
      "verifiedProcessing",
    ]);
    expect(
      notices(
        "fs.processArtifacts",
        { archivePolicy: "autoDetect" },
        { requirement: "unknown", steps: [] },
      ),
    ).toContain("autoDetect");
    expect(
      notices(
        "fs.processArtifacts",
        { archivePolicy: "keep" },
        { requirement: "unknown", steps: [] },
      ),
    ).toContain("keep");
  });

  it("derives retention and delivery from each concrete node configuration", () => {
    expect(notices("acquisition.materialize", {})).toContain("cleanWorkingDirectory");
    expect(notices("acquisition.materialize", { cleanWorkingDirectory: false })).toContain(
      "keepWorkingDirectory",
    );
    expect(notices("download.cleanupArtifacts", {})).toEqual(["keepInputs"]);
    expect(notices("download.cleanupArtifacts", { policy: "quarantine" })).toEqual(["quarantine"]);
    expect(notices("fs.publishArtifacts", {})).toEqual(["publishNeedsDirectory"]);
    const result = localFileWorkflowPreview(
      { activities: [node("fs.publishArtifacts", { targetDirectory: "/user/result" })] },
      complete,
    );
    expect(result[0].notices).toEqual([{ key: "publish", values: { directory: "/user/result" } }]);
  });

  it("reports unreadable configuration instead of substituting an executable default", () => {
    const result = localFileWorkflowPreview(
      { activities: [{ ...node("fs.publishArtifacts"), configJson: "invalid" }] },
      complete,
    );
    expect(result[0].configValid).toBe(false);
    expect(result[0].notices).toEqual([{ key: "invalidConfig", warning: true }]);
  });
});
