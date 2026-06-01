import type { AppNotificationSeverity } from "@/sdk/constants";

export interface CreateNotificationConfig {
  /** Required. Supports {{field}} / {{outer.inner}} interpolation against the item. */
  title: string;
  /** Optional, same interpolation rules. */
  body: string;
  severity: AppNotificationSeverity;
}
