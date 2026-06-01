import type { WorkflowActivityUI } from "../types";
import type { AiTransformConfig } from "./types";

import ConfigForm from "./ConfigForm";
import Summary from "./Summary";

import { WorkflowActivityCategory } from "@/sdk/constants";

const DEFAULT: AiTransformConfig = { targetItemType: "", extraInstructions: "" };

export const AiTransformUI: WorkflowActivityUI<AiTransformConfig> = {
  kind: "transform.ai.transform",
  displayNameKey: "workflow.activity.aiTransform.displayName",
  category: WorkflowActivityCategory.Transform,
  defaultConfig: () => ({ ...DEFAULT }),
  parseConfig: (json) => {
    if (!json) return { ...DEFAULT };
    try {
      const parsed = JSON.parse(json) as Partial<AiTransformConfig>;
      return {
        targetItemType: parsed.targetItemType ?? "",
        extraInstructions: parsed.extraInstructions ?? "",
      };
    } catch {
      return { ...DEFAULT };
    }
  },
  serializeConfig: (cfg) => JSON.stringify(cfg),
  // No isValid penalty when targetItemType is empty — the chain walker will infer from the
  // next step and flag invalid only when nothing resolves.
  isValid: () => true,
  // Match the backend: prefer the user-pinned target; otherwise let chainWalk fall back to
  // the next activity's single accepted type (chainWalk handles the fallback when this
  // returns null).
  resolveAdaptedOutputType: (configJson, nextActivityRequiredType) => {
    try {
      const cfg = JSON.parse(configJson) as Partial<AiTransformConfig>;
      const pinned = cfg.targetItemType?.trim();
      return pinned ? pinned : nextActivityRequiredType;
    } catch {
      return nextActivityRequiredType;
    }
  },
  ConfigForm,
  Summary,
};
