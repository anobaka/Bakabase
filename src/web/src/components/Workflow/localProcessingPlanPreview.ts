import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Definition } from "@/sdk/Api";

export type LocalFileInstructions = {
  requirement?: string;
  steps?: readonly {
    op?: string;
    selector?: string | null;
    extension?: string | null;
    targetName?: string | null;
    targetDirectory?: string | null;
    sourceDirectory?: string | null;
    findText?: string | null;
    replaceText?: string | null;
  }[];
};

export type LocalStepNotice = {
  key: string;
  values?: Record<string, string | number>;
  warning?: boolean;
};

/** Read-only presentation of declared nodes. It neither prepares payloads nor guesses missing actions. */
export function localFileWorkflowPreview(
  workflow: Pick<Definition, "activities">,
  instructions?: LocalFileInstructions | null,
  alreadyProcessed = false,
) {
  const incomplete =
    !alreadyProcessed &&
    (!instructions ||
      !["required", "notRequired"].includes(instructions.requirement ?? "") ||
      (instructions.requirement === "required" && !instructions.steps?.length));

  return [...(workflow.activities ?? [])]
    .sort((left, right) => left.order - right.order)
    .map((activity) => {
      const notices: LocalStepNotice[] = [];
      let config: Record<string, unknown> = {};
      let configValid = true;
      try {
        const value: unknown = JSON.parse(activity.configJson || "{}");
        if (!value || typeof value !== "object" || Array.isArray(value))
          throw new Error("Invalid config");
        config = value as Record<string, unknown>;
      } catch {
        configValid = false;
        notices.push({ key: "invalidConfig", warning: true });
      }
      if (configValid) {
        switch (activity.kind) {
          case "acquisition.unpack":
            notices.push({
              key: alreadyProcessed
                ? "alreadyProcessed"
                : incomplete
                  ? "waitForInstructions"
                  : instructions?.requirement === "notRequired"
                    ? "notRequired"
                    : "savedInstructions",
              warning: incomplete,
            });
            // This entry always supplies a plan. Unpack's legacy automatic-detection branch
            // (and its deleteArchive flag) is not selected when a plan is present.
            notices.push({ key: "unpackWorkingCopy" });
            break;
          case "fs.processArtifacts":
          case "download.processFiles": {
            const policy = config.archivePolicy ?? "instructions";
            notices.push({
              key:
                policy === "keep"
                  ? "keep"
                  : policy === "autoDetect"
                    ? "autoDetect"
                    : incomplete
                      ? "waitForInstructions"
                      : alreadyProcessed
                        ? "alreadyProcessed"
                        : instructions?.requirement === "notRequired"
                          ? "notRequired"
                          : "savedInstructions",
              warning:
                (policy === "instructions" && incomplete) ||
                (config.archivePolicy == null && incomplete),
            });
            if (
              config.recursive &&
              typeof config.recursive === "object" &&
              (config.recursive as { enabled?: boolean }).enabled
            )
              notices.push({ key: "recursive" });
            notices.push({ key: "verifiedProcessing" });
            break;
          }
          case "fs.publishArtifacts":
            notices.push(
              typeof config.targetDirectory === "string" && config.targetDirectory.trim()
                ? { key: "publish", values: { directory: config.targetDirectory } }
                : { key: "publishNeedsDirectory", warning: true },
            );
            break;
          case "acquisition.place":
            notices.push({ key: "placement" });
            break;
          case "acquisition.materialize":
            notices.push({ key: "materialize" });
            notices.push({
              key:
                config.cleanWorkingDirectory === false
                  ? "keepWorkingDirectory"
                  : "cleanWorkingDirectory",
              warning: config.cleanWorkingDirectory !== false,
            });
            break;
          case "download.cleanupArtifacts":
            notices.push({
              key: config.policy === "quarantine" ? "quarantine" : "keepInputs",
              warning: config.policy === "quarantine",
            });
            break;
        }
      }
      return { activity, config, configValid, notices };
    });
}
