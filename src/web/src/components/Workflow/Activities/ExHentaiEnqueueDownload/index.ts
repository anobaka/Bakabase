import type { WorkflowActivityUI } from "../types";
import type { ExHentaiEnqueueDownloadConfig } from "./types";

import ConfigForm from "./ConfigForm";
import Summary from "./Summary";

import { WorkflowActivityCategory } from "@/sdk/constants";

const DEFAULT: ExHentaiEnqueueDownloadConfig = { intervalMs: 1000, autoRetry: true };

export const ExHentaiEnqueueDownloadUI: WorkflowActivityUI<ExHentaiEnqueueDownloadConfig> = {
  kind: "action.downloader.exhentai.enqueue",
  displayNameKey: "workflow.activity.exhentaiEnqueueDownload.displayName",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ ...DEFAULT }),
  parseConfig: (json) => {
    if (!json) return { ...DEFAULT };
    try {
      const parsed = JSON.parse(json) as Partial<ExHentaiEnqueueDownloadConfig>;
      return {
        intervalMs: Math.max(1000, parsed.intervalMs ?? DEFAULT.intervalMs),
        autoRetry: parsed.autoRetry ?? DEFAULT.autoRetry,
      };
    } catch {
      return { ...DEFAULT };
    }
  },
  serializeConfig: (cfg) => JSON.stringify(cfg),
  isValid: (cfg) => cfg.intervalMs >= 1000,
  ConfigForm,
  Summary,
};
