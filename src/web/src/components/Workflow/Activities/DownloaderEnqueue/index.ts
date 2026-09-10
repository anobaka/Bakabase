import type { WorkflowActivityUI } from "../types";
import type { DownloaderEnqueueConfig } from "./types";

import ConfigForm from "./ConfigForm";
import Summary from "./Summary";

import { WorkflowActivityCategory } from "@/sdk/constants";

const DEFAULT: DownloaderEnqueueConfig = { intervalMs: 1000, autoRetry: true };

export const DownloaderEnqueueUI: WorkflowActivityUI<DownloaderEnqueueConfig> = {
  kind: "action.downloader.enqueue",
  displayNameKey: "workflow.activity.downloaderEnqueue.displayName",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ ...DEFAULT }),
  parseConfig: (json) => {
    if (!json) return { ...DEFAULT };
    try {
      const parsed = JSON.parse(json) as Partial<DownloaderEnqueueConfig>;

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
