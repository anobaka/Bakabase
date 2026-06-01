import type { WorkflowActivityUI } from "../types";
import type { CreateNotificationConfig } from "./types";

import ConfigForm from "./ConfigForm";
import Summary from "./Summary";

import {
  AppNotificationSeverity,
  WorkflowActivityCategory,
} from "@/sdk/constants";

const DEFAULT: CreateNotificationConfig = {
  title: "",
  body: "",
  severity: AppNotificationSeverity.Info,
};

export const CreateNotificationUI: WorkflowActivityUI<CreateNotificationConfig> = {
  kind: "action.notification.create",
  displayNameKey: "workflow.activity.createNotification.displayName",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ ...DEFAULT }),
  parseConfig: (json) => {
    if (!json) return { ...DEFAULT };
    try {
      const parsed = JSON.parse(json) as Partial<CreateNotificationConfig>;
      return {
        title: parsed.title ?? "",
        body: parsed.body ?? "",
        severity: (parsed.severity as AppNotificationSeverity) ?? AppNotificationSeverity.Info,
      };
    } catch {
      return { ...DEFAULT };
    }
  },
  serializeConfig: (cfg) => JSON.stringify(cfg),
  // Empty title = useless notification; flag it as invalid so save can't proceed.
  isValid: (cfg) => cfg.title.trim().length > 0,
  ConfigForm,
  Summary,
};
