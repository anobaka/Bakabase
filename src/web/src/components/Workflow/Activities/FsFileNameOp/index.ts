import type { WorkflowActivityUI } from "../types";
import type { FsFileNameOpConfig } from "./types";

import ConfigForm from "./ConfigForm";
import Summary from "./Summary";

import { WorkflowActivityCategory } from "@/sdk/constants";

const EMPTY: FsFileNameOpConfig = { operations: [] };

export const FsFileNameOpUI: WorkflowActivityUI<FsFileNameOpConfig> = {
  kind: "transform.fs.fileNameOp",
  displayNameKey: "workflow.activity.fsFileNameOp.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ operations: [] }),
  parseConfig: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<FsFileNameOpConfig>;

      return { operations: parsed.operations ?? [] };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: (config) => (config.operations ?? []).length > 0,
  ConfigForm,
  Summary,
};
